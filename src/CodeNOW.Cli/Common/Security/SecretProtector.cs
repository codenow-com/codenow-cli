using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace CodeNOW.Cli.Common.Security;

/// <summary>
/// Encrypts and decrypts sensitive configuration values.
/// Format:
///   enc:v1:&lt;base64(payload)&gt;
///
/// Payload layout (v1):
///   [0..15]   salt (16 bytes)
///   [16..19]  PBKDF2 iterations (uint32, big-endian)
///   [20..31]  AES-GCM nonce (12 bytes)
///   [32..47]  AES-GCM authentication tag (16 bytes)
///   [48..]    ciphertext (UTF-8)
/// </summary>
public static class SecretProtector
{
    /// <summary>
    /// Prefix used to mark encrypted values.
    /// </summary>
    public const string Prefix = "enc:v1:";

    // Crypto parameters (current best practice)
    private const int SaltSize = 16;        // bytes
    private const int NonceSize = 12;       // bytes (GCM recommendation)
    private const int TagSize = 16;         // bytes
    private const int KeySize = 32;         // 256-bit
    private const int DefaultIterations = 200_000;

    /// <summary>
    /// Encrypts plaintext using PBKDF2(SHA-256) + AES-256-GCM.
    /// </summary>
    /// <param name="plaintext">Value to encrypt.</param>
    /// <param name="passphrase">Passphrase used to derive the encryption key.</param>
    /// <param name="iterations">PBKDF2 iteration count.</param>
    /// <returns>Encrypted string with the supported prefix.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="plaintext"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="passphrase"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="iterations"/> is too low.</exception>
    public static string EncryptToString(
        string plaintext,
        string passphrase,
        int iterations = DefaultIterations)
    {
        if (plaintext is null)
            throw new ArgumentNullException(nameof(plaintext));

        if (string.IsNullOrWhiteSpace(passphrase))
            throw new ArgumentException("Passphrase cannot be empty.", nameof(passphrase));

        if (iterations < 50_000)
            throw new ArgumentOutOfRangeException(nameof(iterations), "Iteration count too low.");

        byte[] salt = RandomBytes(SaltSize);
        byte[] nonce = RandomBytes(NonceSize);

        byte[] key = DeriveKey(passphrase, salt, iterations);
        byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        byte[] ciphertext = new byte[plaintextBytes.Length];
        byte[] tag = new byte[TagSize];

        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

            var payload = new byte[
                SaltSize +
                sizeof(uint) +
                NonceSize +
                TagSize +
                ciphertext.Length
            ];

            Buffer.BlockCopy(salt, 0, payload, 0, SaltSize);
            BinaryPrimitives.WriteUInt32BigEndian(
                payload.AsSpan(SaltSize, sizeof(uint)),
                (uint)iterations);

            Buffer.BlockCopy(nonce, 0, payload, SaltSize + sizeof(uint), NonceSize);
            Buffer.BlockCopy(tag, 0, payload, SaltSize + sizeof(uint) + NonceSize, TagSize);
            Buffer.BlockCopy(
                ciphertext,
                0,
                payload,
                SaltSize + sizeof(uint) + NonceSize + TagSize,
                ciphertext.Length);

            return Prefix + Convert.ToBase64String(payload);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintextBytes);
        }
    }

    /// <summary>
    /// Decrypts a value produced by <see cref="EncryptToString"/>.
    /// </summary>
    /// <param name="encryptedValue">Encrypted value with the supported prefix.</param>
    /// <param name="passphrase">Passphrase used to derive the encryption key.</param>
    /// <returns>Decrypted plaintext.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="encryptedValue"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="passphrase"/> is empty.</exception>
    /// <exception cref="FormatException">Thrown when the payload format is invalid.</exception>
    /// <exception cref="CryptographicException">Thrown when decryption fails.</exception>
    public static string DecryptToString(string encryptedValue, string passphrase)
    {
        if (encryptedValue is null)
            throw new ArgumentNullException(nameof(encryptedValue));

        if (string.IsNullOrWhiteSpace(passphrase))
            throw new ArgumentException("Passphrase cannot be empty.", nameof(passphrase));

        if (!encryptedValue.StartsWith(Prefix, StringComparison.Ordinal))
            throw new FormatException("Value does not contain a supported encryption prefix.");

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(
                encryptedValue.Substring(Prefix.Length));
        }
        catch (FormatException ex)
        {
            throw new FormatException("Encrypted value is not valid base64.", ex);
        }

        int minSize = SaltSize + sizeof(uint) + NonceSize + TagSize;
        if (payload.Length < minSize)
            throw new FormatException("Encrypted payload is invalid or corrupted.");

        var salt = payload.AsSpan(0, SaltSize).ToArray();
        int iterations = (int)BinaryPrimitives.ReadUInt32BigEndian(
            payload.AsSpan(SaltSize, sizeof(uint)));

        if (iterations < 50_000)
            throw new FormatException("Invalid PBKDF2 iteration count.");

        int nonceOffset = SaltSize + sizeof(uint);
        var nonce = payload.AsSpan(nonceOffset, NonceSize).ToArray();

        int tagOffset = nonceOffset + NonceSize;
        var tag = payload.AsSpan(tagOffset, TagSize).ToArray();

        int ciphertextOffset = tagOffset + TagSize;
        int ciphertextLength = payload.Length - ciphertextOffset;

        var ciphertext = payload.AsSpan(ciphertextOffset, ciphertextLength).ToArray();
        var plaintextBytes = new byte[ciphertextLength];

        byte[] key = DeriveKey(passphrase, salt, iterations);

        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintextBytes);
            return Encoding.UTF8.GetString(plaintextBytes);
        }
        catch (CryptographicException ex)
        {
            throw new CryptographicException(
                "Failed to decrypt secret. The key may be incorrect or the data was modified.",
                ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintextBytes);
        }
    }

    /// <summary>
    /// Encrypts value only if it is not null or empty.
    /// </summary>
    /// <param name="value">Value to encrypt.</param>
    /// <param name="passphrase">Passphrase used to derive the encryption key.</param>
    /// <param name="iterations">PBKDF2 iteration count.</param>
    /// <returns>Encrypted value or the original value when empty.</returns>
    public static string? EncryptIfNotEmpty(
        string? value,
        string passphrase,
        int iterations = DefaultIterations)
        => string.IsNullOrEmpty(value)
            ? value
            : EncryptToString(value, passphrase, iterations);

    /// <summary>
    /// Decrypts value only if it uses the supported encryption prefix.
    /// </summary>
    /// <param name="value">Value to decrypt.</param>
    /// <param name="passphrase">Passphrase used to derive the encryption key.</param>
    /// <returns>Decrypted value or the original value when not encrypted.</returns>
    public static string? DecryptIfEncrypted(string? value, string passphrase)
        => value is not null && value.StartsWith(Prefix, StringComparison.Ordinal)
            ? DecryptToString(value, passphrase)
            : value;

    private static byte[] DeriveKey(string passphrase, byte[] salt, int iterations)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(passphrase);

        try
        {
            return Rfc2898DeriveBytes.Pbkdf2(
                passwordBytes,
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                KeySize); // output length in bytes
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    private static byte[] RandomBytes(int length)
    {
        var bytes = new byte[length];
        RandomNumberGenerator.Fill(bytes);
        return bytes;
    }
}
