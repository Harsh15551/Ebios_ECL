namespace Ebios.Application.Interfaces;

public interface IBiometricCryptoService
{
    /// <summary>
    /// Encrypts raw biometric image or document bytes using AES-256-GCM authenticated encryption.
    /// Format: [Magic "EBENC1" (6B)][Nonce (12B)][Tag (16B)][Ciphertext]
    /// </summary>
    byte[] Encrypt(byte[] plainBytes);

    /// <summary>
    /// Decrypts encrypted bytes. Throws CryptographicException if tag/integrity verification fails.
    /// </summary>
    byte[] Decrypt(byte[] cipherBytes);

    /// <summary>
    /// Checks whether the byte stream starts with the authenticated biometric vault magic header "EBENC1".
    /// </summary>
    bool IsEncrypted(ReadOnlySpan<byte> bytes);

    /// <summary>
    /// Returns decrypted plaintext if file is encrypted with EBENC1, otherwise returns original bytes (backward compatibility).
    /// </summary>
    byte[] DecryptIfEncrypted(byte[] fileBytes);
}

