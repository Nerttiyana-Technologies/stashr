using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Stashr.Core;
using Stashr.Core.Cryptography;
using Stashr.Core.Model;
using Stashr.Core.Storage;
using Stashr.Crypto;

namespace Stashr.Engine.Seal;

/// <summary>Thrown when submitted unseal shares do not reconstruct the master key (ADR-0002).</summary>
public sealed class UnsealFailedException() : StashrException("unseal failed: invalid key shares");

/// <summary>Returned once at initialization; the shares are shown to the operator and never stored.</summary>
public sealed record InitResult
{
    public required IReadOnlyList<string> Shares { get; init; }
    public required int Threshold { get; init; }
    public required int TotalShares { get; init; }
}

/// <summary>
/// Owns the engine's seal lifecycle and envelope encryption (ADR-0002/0004/0012). The master
/// key is split with Shamir into N shares (M required to unseal); it wraps a data key (DEK)
/// that encrypts secret values. The master and DEK live only in locked, zeroed
/// <see cref="SecureMemory"/> while unsealed (ADR-0015) and never touch the store in plaintext.
/// </summary>
public sealed class KeyManager(ISecretStore store, ICryptoProvider crypto, IAutoUnsealProvider? autoUnseal = null) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<byte[]> _pending = new();

    private SecureMemory? _master;
    private SecureMemory? _dek;
    private int _activeKeyVersion;
    private int _threshold;
    private int _total;

    public bool IsInitialized { get; private set; }
    public bool IsSealed => _master is null || _dek is null;
    public int ActiveKeyVersion => _activeKeyVersion;

    /// <summary>Read persisted seal config at startup so we know init state + threshold (stays sealed).</summary>
    public async Task LoadStateAsync(CancellationToken ct = default)
    {
        var cfg = await store.GetSealConfigAsync(ct);
        if (cfg is null) { IsInitialized = false; return; }
        var doc = JsonSerializer.Deserialize<SealConfig>(Encoding.UTF8.GetString(cfg));
        if (doc is not null)
        {
            _threshold = doc.Threshold;
            _total = doc.TotalShares;
            IsInitialized = true;
        }
    }

    public async Task<InitResult> InitializeAsync(int totalShares, int threshold, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (IsInitialized) throw new StashrException("stashr is already initialized");
            if (threshold < 2 || threshold > totalShares)
                throw new ArgumentException("require 2 <= threshold <= totalShares");

            var master = new byte[OsCryptoProvider.KeySize];
            var dek = new byte[OsCryptoProvider.KeySize];
            crypto.GetRandomBytes(master);
            crypto.GetRandomBytes(dek);
            try
            {
                var wrapped = crypto.Encrypt(dek, master);
                await store.PutKeyAsync(
                    new WrappedKey { Version = 1, Wrapped = wrapped, State = "active", CreatedAt = DateTimeOffset.UtcNow }, ct);

                var cfg = new SealConfig
                {
                    Mode = autoUnseal is not null ? "auto" : "shamir",
                    Threshold = threshold,
                    TotalShares = totalShares,
                    WrappedMaster = autoUnseal is not null ? Convert.ToBase64String(autoUnseal.Wrap(master)) : null,
                };
                await store.PutSealConfigAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cfg)), ct);

                var shares = ShamirSecretSharing.Split(master, totalShares, threshold, crypto)
                    .Select(Convert.ToHexString)
                    .ToList();

                _master = SecureMemory.From(master);
                _dek = SecureMemory.From(dek);
                _activeKeyVersion = 1;
                _threshold = threshold;
                _total = totalShares;
                IsInitialized = true;

                return new InitResult { Shares = shares, Threshold = threshold, TotalShares = totalShares };
            }
            finally
            {
                CryptographicOperations.ZeroMemory(master);
                CryptographicOperations.ZeroMemory(dek);
            }
        }
        finally { _gate.Release(); }
    }

    public async Task<SealStatus> SubmitUnsealShareAsync(string shareHex, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!IsInitialized) throw new StashrException("stashr is not initialized");
            if (!IsSealed) return BuildStatus();

            _pending.Add(Convert.FromHexString(shareHex));
            if (_pending.Count < _threshold) return BuildStatus();

            byte[] master;
            try { master = ShamirSecretSharing.Combine(_pending); }
            catch (Exception) { _pending.Clear(); throw new UnsealFailedException(); }

            try
            {
                var active = await store.GetActiveKeyAsync(ct)
                    ?? throw new StashrException("no active key in store");

                byte[] dek;
                try { dek = crypto.Decrypt(active.Wrapped, master); }
                catch (CryptographicException) { _pending.Clear(); throw new UnsealFailedException(); }

                _master = SecureMemory.From(master);
                _dek = SecureMemory.From(dek);
                _activeKeyVersion = active.Version;
                CryptographicOperations.ZeroMemory(dek);
                _pending.Clear();
                return BuildStatus();
            }
            finally { CryptographicOperations.ZeroMemory(master); }
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Attempt auto-unseal via the configured provider (ADR-0012) — no operator shares. Returns
    /// true on success. No-op/false if no provider is configured or the deployment was
    /// initialized in Shamir mode.
    /// </summary>
    public async Task<bool> TryAutoUnsealAsync(CancellationToken ct = default)
    {
        if (autoUnseal is null) return false;
        await _gate.WaitAsync(ct);
        try
        {
            if (!IsSealed) return true;

            var cfgBytes = await store.GetSealConfigAsync(ct);
            if (cfgBytes is null) return false;
            var cfg = JsonSerializer.Deserialize<SealConfig>(Encoding.UTF8.GetString(cfgBytes));
            if (cfg is null || cfg.Mode != "auto" || cfg.WrappedMaster is null) return false;

            byte[] master;
            try { master = autoUnseal.Unwrap(Convert.FromBase64String(cfg.WrappedMaster)); }
            catch { return false; }

            try
            {
                var active = await store.GetActiveKeyAsync(ct)
                    ?? throw new StashrException("no active key in store");

                byte[] dek;
                try { dek = crypto.Decrypt(active.Wrapped, master); }
                catch (CryptographicException) { return false; }

                _master = SecureMemory.From(master);
                _dek = SecureMemory.From(dek);
                _activeKeyVersion = active.Version;
                CryptographicOperations.ZeroMemory(dek);
                return true;
            }
            finally { CryptographicOperations.ZeroMemory(master); }
        }
        finally { _gate.Release(); }
    }

    public void Seal()
    {
        _master?.Dispose(); _master = null;
        _dek?.Dispose(); _dek = null;
        _pending.Clear();
    }

    public SealStatus Status() => BuildStatus();

    /// <summary>Encrypt a secret value under the active DEK. Throws if sealed.</summary>
    public SealedBlob EncryptValue(ReadOnlySpan<byte> plaintext)
        => _dek is null ? throw new SealedException() : crypto.Encrypt(plaintext, _dek.Span);

    /// <summary>Decrypt a secret value under the active DEK. Throws if sealed.</summary>
    public byte[] DecryptValue(SealedBlob blob)
        => _dek is null ? throw new SealedException() : crypto.Decrypt(blob, _dek.Span);

    /// <summary>
    /// Derive a stable purpose-specific subkey from the active DEK via HMAC (e.g. the audit
    /// chain key, the token HMAC key). Deterministic while unsealed; throws if sealed.
    /// </summary>
    public byte[] DeriveSubkey(string label)
        => _dek is null
            ? throw new SealedException()
            : crypto.Hmac(System.Text.Encoding.UTF8.GetBytes(label), _dek.Span);

    private SealStatus BuildStatus() => new()
    {
        Initialized = IsInitialized,
        Sealed = IsSealed,
        Threshold = _threshold,
        TotalShares = _total,
        Progress = _pending.Count,
        HaRole = IsSealed ? "sealed" : "active",
    };

    public void Dispose()
    {
        _master?.Dispose();
        _dek?.Dispose();
        _gate.Dispose();
    }

    private sealed record SealConfig
    {
        public string Mode { get; init; } = "shamir";
        public int Threshold { get; init; }
        public int TotalShares { get; init; }
        public string? WrappedMaster { get; init; }
    }
}
