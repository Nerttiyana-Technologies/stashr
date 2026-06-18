using System.Text;
using System.Text.Json;
using Stashr.Core.Audit;
using Stashr.Core.Model;
using Stashr.Core.Cryptography;
using Stashr.Core.Storage;
using Stashr.Engine.Seal;

namespace Stashr.Engine.Audit;

/// <summary>Result of verifying the audit hash-chain (ADR-0005).</summary>
public sealed record AuditVerifyResult
{
    public required bool Valid { get; init; }
    public long? FirstBrokenSeq { get; init; }
    public long Checked { get; init; }
}

/// <summary>
/// Appends audit entries as a tamper-evident, keyed HMAC hash-chain (ADR-0005):
/// <c>chain_hash[n] = HMAC(chainKey, chain_hash[n-1] || canonical(entry[n]))</c>. The chain key
/// is derived from the DEK (so an attacker who edits the store cannot forge the chain). The
/// genesis seed is bound to the active key version. Fail-closed: if a required sink cannot
/// record, the append throws.
/// </summary>
public sealed class AuditChain(
    ISecretStore store,
    ICryptoProvider crypto,
    KeyManager keys,
    IReadOnlyList<IAuditSink>? sinks = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IReadOnlyList<IAuditSink> _sinks = sinks ?? Array.Empty<IAuditSink>();
    private readonly SemaphoreSlim _gate = new(1, 1);

    private long _lastSeq;
    private string? _lastHash;

    /// <summary>Load the current chain head so appends continue an existing chain.</summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        _lastSeq = await store.GetLastAuditSeqAsync(ct);
        _lastHash = null;
        if (_lastSeq > 0)
            await foreach (var e in store.ReadAuditAsync(_lastSeq, ct))
                _lastHash = e.ChainHash;
    }

    public async Task<AuditEntry> AppendAsync(AuditEntry entry, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var chainKey = keys.DeriveSubkey("audit-chain");
            var seq = _lastSeq + 1;
            var staged = entry with
            {
                Seq = seq,
                Time = entry.Time == default ? DateTimeOffset.UtcNow : entry.Time,
                ChainHash = null,
            };

            var prev = _lastHash is null ? Genesis(chainKey) : Convert.FromBase64String(_lastHash);
            var canonical = JsonSerializer.SerializeToUtf8Bytes(staged, Json);
            var hash = Convert.ToBase64String(crypto.Hmac(Concat(prev, canonical), chainKey));

            var final = staged with { ChainHash = hash };
            await store.AppendAuditAsync(final, ct);

            foreach (var sink in _sinks)
            {
                try { await sink.WriteAsync(final, ct); }
                catch when (!sink.Required) { /* best-effort sink: swallow */ }
                // Required sinks: exception propagates → fail-closed.
            }

            _lastSeq = seq;
            _lastHash = hash;
            return final;
        }
        finally { _gate.Release(); }
    }

    public async Task<AuditVerifyResult> VerifyAsync(CancellationToken ct = default)
    {
        var chainKey = keys.DeriveSubkey("audit-chain");
        string? prevHash = null;
        long checkd = 0;

        await foreach (var entry in store.ReadAuditAsync(1, ct))
        {
            checkd++;
            var prev = prevHash is null ? Genesis(chainKey) : Convert.FromBase64String(prevHash);
            var canonical = JsonSerializer.SerializeToUtf8Bytes(entry with { ChainHash = null }, Json);
            var expected = Convert.ToBase64String(crypto.Hmac(Concat(prev, canonical), chainKey));

            if (!string.Equals(expected, entry.ChainHash, StringComparison.Ordinal))
                return new AuditVerifyResult { Valid = false, FirstBrokenSeq = entry.Seq, Checked = checkd };

            prevHash = entry.ChainHash;
        }

        return new AuditVerifyResult { Valid = true, Checked = checkd };
    }

    private byte[] Genesis(byte[] chainKey)
        => crypto.Hmac(Encoding.UTF8.GetBytes($"stashr-genesis|kv{keys.ActiveKeyVersion}"), chainKey);

    private static byte[] Concat(byte[] a, byte[] b)
    {
        var r = new byte[a.Length + b.Length];
        Buffer.BlockCopy(a, 0, r, 0, a.Length);
        Buffer.BlockCopy(b, 0, r, a.Length, b.Length);
        return r;
    }
}
