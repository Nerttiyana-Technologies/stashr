using Stashr.Crypto;
using Xunit;

namespace Stashr.Crypto.Tests;

public class ShamirTests
{
    private readonly OsCryptoProvider _crypto = new();

    [Fact]
    public void Split_then_combine_with_threshold_recovers_secret()
    {
        var secret = new byte[32];
        _crypto.GetRandomBytes(secret);

        var shares = ShamirSecretSharing.Split(secret, n: 5, m: 3, _crypto);

        // Any 3 of the 5 shares must reconstruct exactly.
        var recovered = ShamirSecretSharing.Combine(new[] { shares[0], shares[2], shares[4] });
        Assert.Equal(secret, recovered);
    }

    [Fact]
    public void Different_threshold_subsets_all_recover_the_same_secret()
    {
        var secret = "the-master-key-bytes-go-here!!!!"u8.ToArray();
        var shares = ShamirSecretSharing.Split(secret, n: 5, m: 3, _crypto);

        Assert.Equal(secret, ShamirSecretSharing.Combine(new[] { shares[1], shares[3], shares[4] }));
        Assert.Equal(secret, ShamirSecretSharing.Combine(new[] { shares[0], shares[1], shares[2] }));
    }

    [Fact]
    public void Fewer_than_threshold_shares_do_not_recover_secret()
    {
        var secret = new byte[32];
        _crypto.GetRandomBytes(secret);
        var shares = ShamirSecretSharing.Split(secret, n: 5, m: 3, _crypto);

        // Two shares (below threshold of 3) must not equal the secret.
        var attempt = ShamirSecretSharing.Combine(new[] { shares[0], shares[1] });
        Assert.NotEqual(secret, attempt);
    }

    [Fact]
    public void Split_rejects_invalid_threshold()
    {
        Assert.Throws<ArgumentException>(() =>
            ShamirSecretSharing.Split(new byte[16], n: 3, m: 4, _crypto));
    }
}
