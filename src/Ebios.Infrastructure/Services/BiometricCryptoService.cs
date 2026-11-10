using System.Security.Cryptography;
using System.Text;
using Ebios.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Ebios.Infrastructure.Services;

public class BiometricCryptoService : IBiometricCryptoService
{
    private static readonly byte[] MagicHeader = Encoding.ASCII.GetBytes("EBENC1"); // 6 bytes
    private const int NonceSize = 12; // Standard AES-GCM nonce length (96 bits)
    private const int TagSize = 16;   // Standard AES-GCM authentication tag (128 bits)
    private const int HeaderTotalSize = 6 + NonceSize + TagSize; // 34 bytes

    private readonly byte[] _key;
    private readonly ILogger<BiometricCryptoService> _logger;

    public BiometricCryptoService(IConfiguration configuration, ILogger<BiometricCryptoService> logger)
    {
        _logger = logger;
        _key = ResolveEncryptionKey(configuration, logger);
    }

    public byte[] Encrypt(byte[] plainBytes)
    {
        ArgumentNullException.ThrowIfNull(plainBytes);

        if (plainBytes.Length == 0)
        {
            return Array.Empty<byte>();
        }

        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);

        var tag = new byte[TagSize];
        var ciphertext = new byte[plainBytes.Length];

        using (var aesGcm = new AesGcm(_key, TagSize))
        {
            aesGcm.Encrypt(nonce, plainBytes, ciphertext, tag);
        }

        var result = new byte[HeaderTotalSize + ciphertext.Length];
        Buffer.BlockCopy(MagicHeader, 0, result, 0, MagicHeader.Length);
        Buffer.BlockCopy(nonce, 0, result, MagicHeader.Length, NonceSize);
        Buffer.BlockCopy(tag, 0, result, MagicHeader.Length + NonceSize, TagSize);
        Buffer.BlockCopy(ciphertext, 0, result, HeaderTotalSize, ciphertext.Length);

        return result;
    }

    public byte[] Decrypt(byte[] cipherBytes)
    {
        ArgumentNullException.ThrowIfNull(cipherBytes);

        if (!IsEncrypted(cipherBytes))
        {
            throw new CryptographicException("The provided byte payload is not encrypted with EBENC1 AES-GCM vault format.");
        }

        ReadOnlySpan<byte> data = cipherBytes;
        var nonce = data.Slice(MagicHeader.Length, NonceSize);
        var tag = data.Slice(MagicHeader.Length + NonceSize, TagSize);
        var ciphertext = data.Slice(HeaderTotalSize);

        var plainBytes = new byte[ciphertext.Length];
        using (var aesGcm = new AesGcm(_key, TagSize))
        {
            aesGcm.Decrypt(nonce, ciphertext, tag, plainBytes);
        }

        return plainBytes;
    }

    public bool IsEncrypted(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderTotalSize)
        {
            return false;
        }

        for (int i = 0; i < MagicHeader.Length; i++)
        {
            if (bytes[i] != MagicHeader[i])
            {
                return false;
            }
        }

        return true;
    }

    public byte[] DecryptIfEncrypted(byte[] fileBytes)
    {
        if (fileBytes == null || fileBytes.Length == 0)
        {
            return Array.Empty<byte>();
        }

        if (!IsEncrypted(fileBytes))
        {
            return fileBytes; // Seamless backward compatibility with unencrypted legacy photos
        }

        try
        {
            return Decrypt(fileBytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt biometric file payload. Key mismatch or corrupted ciphertext.");
            throw;
        }
    }

    private static byte[] ResolveEncryptionKey(IConfiguration configuration, ILogger logger)
    {
        var rawKey = configuration["BIOMETRIC_FILE_ENCRYPTION_KEY"]
                     ?? Environment.GetEnvironmentVariable("BIOMETRIC_FILE_ENCRYPTION_KEY");

        if (!string.IsNullOrWhiteSpace(rawKey))
        {
            rawKey = rawKey.Trim();

            // Check if base64 32 bytes (44 chars with '=')
            if (rawKey.Length == 44)
            {
                try
                {
                    var b64Bytes = Convert.FromBase64String(rawKey);
                    if (b64Bytes.Length == 32)
                    {
                        logger.LogInformation("Loaded 256-bit AES biometric encryption key from Base64 configuration.");
                        return b64Bytes;
                    }
                }
                catch { /* fallback to hash */ }
            }

            // Check if 64 hex characters (32 bytes)
            if (rawKey.Length == 64 && IsHexString(rawKey))
            {
                try
                {
                    logger.LogInformation("Loaded 256-bit AES biometric encryption key from Hex configuration.");
                    return Convert.FromHexString(rawKey);
                }
                catch { /* fallback to hash */ }
            }

            // Derive standard 32-byte (256-bit) key via SHA-256
            logger.LogInformation("Derived 256-bit AES biometric encryption key using SHA-256 hash of configured passphrase.");
            return SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
        }

        logger.LogWarning("BIOMETRIC_FILE_ENCRYPTION_KEY not found in configuration or .env. Using fallback application vault key.");
        return SHA256.HashData(Encoding.UTF8.GetBytes("EclEbios_Biometric_Vault_Key_2026_Secure_AesGcm!"));
    }

    private static bool IsHexString(string s)
    {
        foreach (char c in s)
        {
            bool isHex = (c >= '0' && c <= '9') ||
                         (c >= 'a' && c <= 'f') ||
                         (c >= 'A' && c <= 'F');
            if (!isHex) return false;
        }
        return true;
    }
}

