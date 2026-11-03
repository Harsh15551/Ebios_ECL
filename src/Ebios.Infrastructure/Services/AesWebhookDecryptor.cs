using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Ebios.Infrastructure.Services;

public static class AesWebhookDecryptor
{
    public static string Decrypt(string encryptedBase64, string password, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(encryptedBase64)) return string.Empty;
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Decryption key/password cannot be empty.", nameof(password));
        }

        // Ensure key is exactly 32 bytes (256-bit) as per eBioServer doc
        string paddedKey = password.PadRight(32, '1');
        if (paddedKey.Length > 32) paddedKey = paddedKey.Substring(0, 32);
        byte[] keyBytes = Encoding.UTF8.GetBytes(paddedKey);

        byte[] cipherBytes = Convert.FromBase64String(encryptedBase64.Trim());

        // Try Strategy 1: IV = first 16 bytes of key (eSSL standard)
        try
        {
            byte[] iv = new byte[16];
            Array.Copy(keyBytes, 0, iv, 0, 16);
            string decrypted = DecryptWithIv(cipherBytes, keyBytes, iv);
            if (decrypted.Contains("EmployeeCode") || decrypted.StartsWith("{") || decrypted.StartsWith("["))
            {
                return decrypted;
            }
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Strategy 1 (Key as IV) failed.");
        }

        // Try Strategy 2: IV = 16 zeros
        try
        {
            byte[] ivZeros = new byte[16];
            string decrypted = DecryptWithIv(cipherBytes, keyBytes, ivZeros);
            if (decrypted.Contains("EmployeeCode") || decrypted.StartsWith("{") || decrypted.StartsWith("["))
            {
                return decrypted;
            }
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Strategy 2 (Zero IV) failed.");
        }

        // Try Strategy 3: IV is prepended to cipherBytes (first 16 bytes)
        try
        {
            if (cipherBytes.Length > 16)
            {
                byte[] iv = new byte[16];
                Array.Copy(cipherBytes, 0, iv, 0, 16);
                byte[] actualCipher = new byte[cipherBytes.Length - 16];
                Array.Copy(cipherBytes, 16, actualCipher, 0, actualCipher.Length);
                string decrypted = DecryptWithIv(actualCipher, keyBytes, iv);
                if (decrypted.Contains("EmployeeCode") || decrypted.StartsWith("{") || decrypted.StartsWith("["))
                {
                    return decrypted;
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Strategy 3 (Prepended IV) failed.");
        }

        throw new InvalidOperationException("Failed to decrypt eBioServer webhook payload with provided key.");
    }

    private static string DecryptWithIv(byte[] cipherBytes, byte[] keyBytes, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = keyBytes;
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        byte[] decryptedBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
        return Encoding.UTF8.GetString(decryptedBytes);
    }
}

