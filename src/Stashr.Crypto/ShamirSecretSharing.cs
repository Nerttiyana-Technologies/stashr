using Stashr.Core.Cryptography;

namespace Stashr.Crypto;

/// <summary>
/// Shamir secret sharing over GF(2^8) (the AES field, reduction polynomial 0x11B) — used to
/// split the root master key into N shares requiring M to reconstruct (ADR-0002/0012).
/// Each share is <c>[x, y0, y1, ...]</c>: a non-zero x-coordinate followed by one y-byte per
/// secret byte. Randomness comes from the validated <see cref="ICryptoProvider"/>.
/// </summary>
public static class ShamirSecretSharing
{
    /// <summary>Split <paramref name="secret"/> into <paramref name="n"/> shares, threshold <paramref name="m"/>.</summary>
    public static IReadOnlyList<byte[]> Split(ReadOnlySpan<byte> secret, int n, int m, ICryptoProvider rng)
    {
        if (m < 2 || m > n) throw new ArgumentException("require 2 <= m <= n");
        if (n > 255) throw new ArgumentException("n must be <= 255");
        if (secret.Length == 0) throw new ArgumentException("secret must be non-empty");

        var shares = new byte[n][];
        for (var s = 0; s < n; s++)
        {
            shares[s] = new byte[secret.Length + 1];
            shares[s][0] = (byte)(s + 1); // x = 1..n (never 0)
        }

        var rnd = new byte[m - 1];
        Span<byte> coeffs = stackalloc byte[m];
        for (var b = 0; b < secret.Length; b++)
        {
            coeffs[0] = secret[b];          // constant term = the secret byte
            rng.GetRandomBytes(rnd);
            for (var i = 1; i < m; i++) coeffs[i] = rnd[i - 1];

            for (var s = 0; s < n; s++)
                shares[s][b + 1] = EvalPoly(coeffs, shares[s][0]);
        }

        return shares;
    }

    /// <summary>Reconstruct the secret from any <c>m</c> distinct shares.</summary>
    public static byte[] Combine(IReadOnlyList<byte[]> shares)
    {
        if (shares.Count < 2) throw new ArgumentException("need at least 2 shares");
        var len = shares[0].Length - 1;
        var k = shares.Count;

        var xs = new byte[k];
        for (var i = 0; i < k; i++)
        {
            if (shares[i].Length != len + 1) throw new ArgumentException("inconsistent share lengths");
            xs[i] = shares[i][0];
            if (xs[i] == 0) throw new ArgumentException("invalid share: x = 0");
        }

        var secret = new byte[len];
        for (var b = 0; b < len; b++)
        {
            byte acc = 0;
            for (var i = 0; i < k; i++)
            {
                byte num = 1, den = 1;
                for (var j = 0; j < k; j++)
                {
                    if (j == i) continue;
                    // Lagrange basis evaluated at x = 0. Subtraction in GF(2^n) is XOR.
                    num = GMul(num, xs[j]);              // (0 - xj) == xj
                    den = GMul(den, (byte)(xs[i] ^ xs[j])); // (xi - xj) == xi ^ xj
                }
                var basis = GMul(num, GInv(den));
                acc ^= GMul(shares[i][b + 1], basis);
            }
            secret[b] = acc;
        }
        return secret;
    }

    private static byte EvalPoly(ReadOnlySpan<byte> coeffs, byte x)
    {
        // Horner's method from the highest-degree coefficient down.
        byte y = 0;
        for (var i = coeffs.Length - 1; i >= 0; i--)
            y = (byte)(GMul(y, x) ^ coeffs[i]);
        return y;
    }

    private static byte GMul(byte a, byte b)
    {
        int aa = a, bb = b, p = 0;
        for (var i = 0; i < 8 && bb != 0; i++)
        {
            if ((bb & 1) != 0) p ^= aa;
            var hi = (aa & 0x80) != 0;
            aa = (aa << 1) & 0xff;
            if (hi) aa ^= 0x1b; // x^8 + x^4 + x^3 + x + 1 (low byte of 0x11B)
            bb >>= 1;
        }
        return (byte)p;
    }

    private static byte GInv(byte a)
    {
        if (a == 0) throw new DivideByZeroException("no inverse for 0 in GF(2^8)");
        // a^(254) = a^(-1) in GF(2^8).
        byte r = 1;
        for (var i = 0; i < 254; i++) r = GMul(r, a);
        return r;
    }
}
