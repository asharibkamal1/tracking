using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using TaxpayerAnalytics.Shared.Configuration;

namespace TaxpayerAnalytics.Shared.Security;

public interface ITrackingTokenService
{
    /// <summary>
    /// Generates a short opaque token (22 base64url chars = 128 bits of entropy).
    /// Stored on TaxpayerRecipient.TrackingToken. Resolution is a unique-index lookup
    /// at the call site; the token itself reveals nothing about the recipient.
    /// Total SMS URL with this token fits well under 160 chars:
    ///   https://iris.fbr.gov.pk/c?t=&lt;22 chars&gt;  ==  ~50 chars
    /// </summary>
    string Issue();

    /// <summary>HMAC-SHA256(ntn, pepper). Deterministic for lookup without decryption.</summary>
    string HashNtn(string ntn);
}

public sealed class TrackingTokenService(IOptions<SecurityOptions> options) : ITrackingTokenService
{
    private readonly byte[] _ntnPepper = Encoding.UTF8.GetBytes(options.Value.NtnHashPepper);

    public string Issue()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        return Base64UrlEncode(bytes);
    }

    public string HashNtn(string ntn)
    {
        using var hmac = new HMACSHA256(_ntnPepper);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(ntn.Trim()));
        return Convert.ToHexString(hash);
    }

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
