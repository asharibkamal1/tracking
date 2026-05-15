using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using TaxpayerAnalytics.Shared.Configuration;

namespace TaxpayerAnalytics.Shared.Security;

public interface IPiiCipher
{
    byte[] Encrypt(string plaintext);
    string Decrypt(byte[] ciphertext);
    string MaskNtn(string ntn);
    string MaskMobile(string mobile);
}

/// <summary>
/// AES-256-GCM column-level encryption for PII at rest. Format: [12 nonce][16 tag][ct].
/// </summary>
public sealed class PiiCipher : IPiiCipher
{
    private readonly byte[] _key;

    public PiiCipher(IOptions<SecurityOptions> options)
    {
        var raw = Encoding.UTF8.GetBytes(options.Value.PiiEncryptionKey);
        _key = SHA256.HashData(raw);
    }

    public byte[] Encrypt(string plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var pt = Encoding.UTF8.GetBytes(plaintext);
        var ct = new byte[pt.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(nonce, pt, ct, tag);

        var output = new byte[12 + 16 + ct.Length];
        Buffer.BlockCopy(nonce, 0, output, 0, 12);
        Buffer.BlockCopy(tag, 0, output, 12, 16);
        Buffer.BlockCopy(ct, 0, output, 28, ct.Length);
        return output;
    }

    public string Decrypt(byte[] ciphertext)
    {
        var nonce = ciphertext.AsSpan(0, 12);
        var tag = ciphertext.AsSpan(12, 16);
        var ct = ciphertext.AsSpan(28);
        var pt = new byte[ct.Length];
        using var aes = new AesGcm(_key, 16);
        aes.Decrypt(nonce, ct, tag, pt);
        return Encoding.UTF8.GetString(pt);
    }

    public string MaskNtn(string ntn)
    {
        var trimmed = ntn.Trim();
        if (trimmed.Length <= 4) return new string('*', trimmed.Length);
        return new string('*', trimmed.Length - 4) + trimmed[^4..];
    }

    public string MaskMobile(string mobile)
    {
        var trimmed = mobile.Trim();
        if (trimmed.Length <= 4) return new string('*', trimmed.Length);
        return trimmed[..2] + new string('*', trimmed.Length - 4) + trimmed[^2..];
    }
}
