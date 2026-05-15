using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TaxpayerAnalytics.Shared.Configuration;

namespace TaxpayerAnalytics.Shared.Security;

public interface ITrackingTokenService
{
    string Issue(long recipientId, long campaignId, DateTime? expiresAt = null);
    bool TryValidate(string token, out TrackingTokenPayload? payload);
    string HashNtn(string ntn);
}

public sealed record TrackingTokenPayload(long RecipientId, long CampaignId, long ExpiresUnix, string Nonce);

/// <summary>
/// AES-256-GCM encrypted, authenticated token. Wire format (url-safe base64):
///   [12 byte nonce][16 byte tag][ciphertext]
/// The token replaces the NTN in the SMS link.
/// </summary>
public sealed class TrackingTokenService : ITrackingTokenService
{
    private readonly byte[] _key;
    private readonly byte[] _ntnPepper;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public TrackingTokenService(IOptions<SecurityOptions> options)
    {
        var opt = options.Value;
        _key = DeriveKey(opt.TokenEncryptionKey, "tax-tracking-token", 32);
        _ntnPepper = Encoding.UTF8.GetBytes(opt.NtnHashPepper);
    }

    public string Issue(long recipientId, long campaignId, DateTime? expiresAt = null)
    {
        var payload = new TrackingTokenPayload(
            recipientId,
            campaignId,
            new DateTimeOffset(expiresAt ?? DateTime.UtcNow.AddDays(90)).ToUnixTimeSeconds(),
            RandomNumberGenerator.GetHexString(16));

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(payload);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var packed = new byte[NonceSize + TagSize + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, packed, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, packed, NonceSize, TagSize);
        Buffer.BlockCopy(ciphertext, 0, packed, NonceSize + TagSize, ciphertext.Length);

        return Base64UrlEncode(packed);
    }

    public bool TryValidate(string token, out TrackingTokenPayload? payload)
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(token)) return false;
        try
        {
            var packed = Base64UrlDecode(token);
            if (packed.Length <= NonceSize + TagSize) return false;

            var nonce = packed.AsSpan(0, NonceSize);
            var tag = packed.AsSpan(NonceSize, TagSize);
            var ciphertext = packed.AsSpan(NonceSize + TagSize);
            var plaintext = new byte[ciphertext.Length];

            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);

            var decoded = JsonSerializer.Deserialize<TrackingTokenPayload>(plaintext);
            if (decoded is null) return false;
            if (DateTimeOffset.FromUnixTimeSeconds(decoded.ExpiresUnix) < DateTimeOffset.UtcNow)
                return false;

            payload = decoded;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public string HashNtn(string ntn)
    {
        using var hmac = new HMACSHA256(_ntnPepper);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(ntn.Trim()));
        return Convert.ToHexString(hash);
    }

    private static byte[] DeriveKey(string secret, string info, int length)
    {
        using var hkdf = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return hkdf.ComputeHash(Encoding.UTF8.GetBytes(info))[..length];
    }

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string s)
    {
        var padded = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(padded + new string('=', (4 - padded.Length % 4) % 4));
    }
}
