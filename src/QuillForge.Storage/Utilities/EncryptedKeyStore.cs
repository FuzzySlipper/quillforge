using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace QuillForge.Storage.Utilities;

/// <summary>
/// Manages a persistent AES-256 encryption key and provides encrypt/decrypt
/// operations for sensitive strings (e.g. API keys). Uses AES-256-GCM
/// (authenticated encryption) with a random nonce per operation.
/// </summary>
public sealed class EncryptedKeyStore
{
    private const int KeySizeBytes = 32;   // AES-256
    private const int NonceSizeBytes = 12; // GCM standard
    private const int TagSizeBytes = 16;   // GCM standard

    private readonly string _keyPath;
    private readonly AtomicFileWriter _writer;
    private readonly ILogger<EncryptedKeyStore> _logger;
    private byte[]? _key;

    public EncryptedKeyStore(string dataDir, AtomicFileWriter writer, ILogger<EncryptedKeyStore> logger)
    {
        _keyPath = Path.Combine(dataDir, "encryption.key");
        _writer = writer;
        _logger = logger;
    }

    /// <summary>
    /// Loads the encryption key from disk, or generates a new one if none exists.
    /// Must be called before Encrypt/Decrypt.
    /// </summary>
    public void Initialize()
    {
        if (File.Exists(_keyPath))
        {
            _key = File.ReadAllBytes(_keyPath);
            if (_key.Length != KeySizeBytes)
            {
                _logger.LogWarning("Encryption key file has unexpected size ({Size} bytes), regenerating", _key.Length);
                _key = null;
            }
        }

        if (_key is null)
        {
            _key = new byte[KeySizeBytes];
            RandomNumberGenerator.Fill(_key);
            // Write synchronously during init (app hasn't started yet)
            _writer.WriteBytesAsync(_keyPath, _key).GetAwaiter().GetResult();

            // Restrict permissions on Linux
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(_keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            _logger.LogInformation("Generated new encryption key at {Path}", _keyPath);
        }
        else
        {
            _logger.LogDebug("Loaded encryption key from {Path}", _keyPath);
        }
    }

    /// <summary>
    /// Encrypts a plaintext string using AES-256-GCM.
    /// Returns a Base64 string containing nonce + ciphertext + tag.
    /// </summary>
    public string Encrypt(string plaintext)
    {
        if (_key is null) throw new InvalidOperationException("EncryptedKeyStore not initialized");
        if (string.IsNullOrEmpty(plaintext)) return "";

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = new byte[NonceSizeBytes];
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSizeBytes];

        using var aes = new AesGcm(_key, TagSizeBytes);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        // Pack as: nonce(12) || ciphertext(N) || tag(16)
        var result = new byte[NonceSizeBytes + ciphertext.Length + TagSizeBytes];
        nonce.CopyTo(result, 0);
        ciphertext.CopyTo(result, NonceSizeBytes);
        tag.CopyTo(result, NonceSizeBytes + ciphertext.Length);

        return Convert.ToBase64String(result);
    }

    /// <summary>
    /// Decrypts a Base64 string produced by <see cref="Encrypt"/>.
    /// </summary>
    public string Decrypt(string encryptedBase64)
    {
        if (_key is null) throw new InvalidOperationException("EncryptedKeyStore not initialized");
        if (string.IsNullOrEmpty(encryptedBase64)) return "";

        var packed = Convert.FromBase64String(encryptedBase64);
        if (packed.Length < NonceSizeBytes + TagSizeBytes)
            throw new CryptographicException("Encrypted data too short");

        var nonce = packed[..NonceSizeBytes];
        var ciphertextLength = packed.Length - NonceSizeBytes - TagSizeBytes;
        var ciphertext = packed[NonceSizeBytes..(NonceSizeBytes + ciphertextLength)];
        var tag = packed[(NonceSizeBytes + ciphertextLength)..];

        var plaintext = new byte[ciphertextLength];

        using var aes = new AesGcm(_key, TagSizeBytes);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }
}
