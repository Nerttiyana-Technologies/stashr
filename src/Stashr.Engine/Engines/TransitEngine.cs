using System.Text.Json;
using Stashr.Core.Cryptography;
using Stashr.Core.Engines;
using Stashr.Core.Storage;
using Stashr.Crypto;
using Stashr.Engine.Seal;

namespace Stashr.Engine.Engines;

/// <summary>
/// Transit: encryption-as-a-service (ADR-0009). Holds named data keys and encrypts/decrypts
/// caller-supplied data <b>without ever storing the plaintext</b>. Each named key is itself
/// wrapped under the engine DEK at rest; user data is sealed under the named key. Requires the
/// engine to be unsealed.
/// </summary>
public sealed class TransitEngine(KeyManager keys, ICryptoProvider crypto) : ISecretsEngine
{
    private const string CipherPrefix = "stashr:v1:";

    public string Type => "transit";

    public async Task<EngineResponse> HandleAsync(EngineRequest request, IStorageView storage, CancellationToken ct = default)
    {
        var slash = request.Path.IndexOf('/');
        var verb = slash < 0 ? request.Path : request.Path.Substring(0, slash);
        var name = slash < 0 ? string.Empty : request.Path.Substring(slash + 1);
        if (string.IsNullOrEmpty(name)) return EngineResponse.Missing;

        switch (verb)
        {
            case "keys":
                if (request.Operation == EngineOp.Write)
                {
                    if (await storage.GetAsync($"keys/{name}", ct) is null)
                    {
                        var material = new byte[OsCryptoProvider.KeySize];
                        crypto.GetRandomBytes(material);
                        var wrapped = keys.EncryptValue(material); // wrap named key under the DEK
                        await storage.PutAsync($"keys/{name}", JsonSerializer.SerializeToUtf8Bytes(wrapped), ct);
                    }
                    return EngineResponse.Empty;
                }
                if (request.Operation == EngineOp.Read)
                {
                    return await storage.GetAsync($"keys/{name}", ct) is not null
                        ? new EngineResponse { Data = new Dictionary<string, string> { ["name"] = name, ["type"] = "aes256-gcm" } }
                        : EngineResponse.Missing;
                }
                return EngineResponse.Missing;

            case "encrypt":
            {
                var key = await LoadKeyAsync(storage, name, ct);
                if (key is null) return EngineResponse.Missing;
                if (request.Data is null || !request.Data.TryGetValue("plaintext", out var plaintextB64))
                    return EngineResponse.Missing;

                var plaintext = Convert.FromBase64String(plaintextB64);
                var blob = crypto.Encrypt(plaintext, key);
                var ciphertext = CipherPrefix + Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(blob));
                return new EngineResponse { Data = new Dictionary<string, string> { ["ciphertext"] = ciphertext } };
            }

            case "decrypt":
            {
                var key = await LoadKeyAsync(storage, name, ct);
                if (key is null) return EngineResponse.Missing;
                if (request.Data is null || !request.Data.TryGetValue("ciphertext", out var ciphertext)
                    || !ciphertext.StartsWith(CipherPrefix, StringComparison.Ordinal))
                    return EngineResponse.Missing;

                var blobJson = Convert.FromBase64String(ciphertext.Substring(CipherPrefix.Length));
                var blob = JsonSerializer.Deserialize<SealedBlob>(blobJson)!;
                var plaintext = crypto.Decrypt(blob, key);
                return new EngineResponse { Data = new Dictionary<string, string> { ["plaintext"] = Convert.ToBase64String(plaintext) } };
            }

            default:
                return EngineResponse.Missing;
        }
    }

    private async Task<byte[]?> LoadKeyAsync(IStorageView storage, string name, CancellationToken ct)
    {
        var stored = await storage.GetAsync($"keys/{name}", ct);
        if (stored is null) return null;
        var wrapped = JsonSerializer.Deserialize<SealedBlob>(stored)!;
        return keys.DecryptValue(wrapped);
    }
}
