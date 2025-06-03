using System.Security.Cryptography;
using System.Text;

/// <summary>
/// 支持的加密协议版本
/// </summary>
public enum EncryptionProtocol
{
    None = 0,         // 无加密
    AesGcm = 1,     // AES-GCM v1
    AesCbcHmac = 2, // AES-CBC + HMAC v1
}

/// <summary>
/// 加密服务接口，定义加密解密操作
/// </summary>
public interface IEncryptionService
{
    Task<byte[]> EncryptAsync(byte[] data);
    Task<byte[]> DecryptAsync(byte[] encryptedData);
    byte[] GenerateInitialIv();
}

/// <summary>
/// AES-GCM加密服务实现
/// </summary>
public class AesGcmEncryptionService : IEncryptionService
{
    private readonly byte[] _key;
    private const int IvSize = 12; // AES-GCM推荐IV长度

    public AesGcmEncryptionService(byte[] key)
    {
        _key = key ?? throw new ArgumentNullException(nameof(key));
    }

    public async Task<byte[]> EncryptAsync(byte[] data)
    {
        if (data == null || data.Length == 0)
            throw new ArgumentException("Data cannot be null or empty", nameof(data));

        var iv = GenerateInitialIv();
        var cipherText = new byte[data.Length];
        var tag = new byte[16]; // 128位标签

        using var aesGcm = new AesGcm(_key);
        aesGcm.Encrypt(iv, data, cipherText, tag);

        var output = new byte[IvSize + 16 + data.Length];
        Buffer.BlockCopy(iv, 0, output, 0, IvSize);
        Buffer.BlockCopy(tag, 0, output, IvSize, 16);
        Buffer.BlockCopy(cipherText, 0, output, IvSize + 16, data.Length);

        return output;
    }

    public async Task<byte[]> DecryptAsync(byte[] encryptedData)
    {
        if (encryptedData == null || encryptedData.Length < IvSize + 16)
            throw new ArgumentException("Invalid encrypted data format", nameof(encryptedData));

        var iv = new byte[IvSize];
        var tag = new byte[16];
        var cipherText = new byte[encryptedData.Length - IvSize - 16];

        Buffer.BlockCopy(encryptedData, 0, iv, 0, IvSize);
        Buffer.BlockCopy(encryptedData, IvSize, tag, 0, 16);
        Buffer.BlockCopy(encryptedData, IvSize + 16, cipherText, 0, cipherText.Length);

        var decryptedData = new byte[cipherText.Length];
        using var aesGcm = new AesGcm(_key);
        aesGcm.Decrypt(iv, cipherText, tag, decryptedData);

        return decryptedData;
    }

    public byte[] GenerateInitialIv()
    {
        var iv = new byte[IvSize];
        RandomNumberGenerator.Fill(iv);
        return iv;
    }
}

/// <summary>
/// AES-CBC + HMAC加密服务实现
/// </summary>
public class AesCbcHmacEncryptionService : IEncryptionService
{
    private readonly byte[] _key;
    private const int IvSize = 16; // AES-CBC IV长度
    private const int HmacSize = 32; // HMAC-SHA256长度

    public AesCbcHmacEncryptionService(byte[] key)
    {
        _key = key ?? throw new ArgumentNullException(nameof(key));
    }

    public async Task<byte[]> EncryptAsync(byte[] data)
    {
        if (data == null || data.Length == 0)
            throw new ArgumentException("Data cannot be null or empty", nameof(data));

        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.BlockSize = 128;
        aes.Key = _key;
        aes.IV = GenerateInitialIv();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var ms = new MemoryStream();
        ms.Write(aes.IV, 0, IvSize);

        using var encryptor = aes.CreateEncryptor();
        using var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write);
        await cs.WriteAsync(data, 0, data.Length);
        await cs.FlushFinalBlockAsync();

        var encryptedData = ms.ToArray();
        var encryptedLength = encryptedData.Length - IvSize;

        var buffer = new byte[IvSize + encryptedLength + HmacSize];
        Buffer.BlockCopy(encryptedData, 0, buffer, 0, IvSize + encryptedLength);

        using var hmac = new HMACSHA256(_key);
        var hmacValue = hmac.ComputeHash(buffer, 0, IvSize + encryptedLength);
        Buffer.BlockCopy(hmacValue, 0, buffer, IvSize + encryptedLength, HmacSize);

        return buffer;
    }

    public async Task<byte[]> DecryptAsync(byte[] encryptedData)
    {
        if (encryptedData == null || encryptedData.Length < IvSize + HmacSize)
            throw new ArgumentException("Invalid encrypted data format", nameof(encryptedData));

        var iv = new byte[IvSize];
        var hmac = new byte[HmacSize];
        var encryptedLength = encryptedData.Length - IvSize - HmacSize;
        var encryptedDataBytes = new byte[encryptedLength];

        Buffer.BlockCopy(encryptedData, 0, iv, 0, IvSize);
        Buffer.BlockCopy(encryptedData, IvSize + encryptedLength, hmac, 0, HmacSize);
        Buffer.BlockCopy(encryptedData, IvSize, encryptedDataBytes, 0, encryptedLength);

        using var hmacAlgorithm = new HMACSHA256(_key);
        var computedHmac = hmacAlgorithm.ComputeHash(encryptedData, 0, IvSize + encryptedLength);

        if (!CryptographicOperations.FixedTimeEquals(computedHmac, hmac))
            throw new CryptographicException("HMAC verification failed");

        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.BlockSize = 128;
        aes.Key = _key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var ms = new MemoryStream(encryptedDataBytes);
        using var decryptor = aes.CreateDecryptor();
        using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
        using var reader = new StreamReader(cs);
        var decryptedText = await reader.ReadToEndAsync();

        return Encoding.UTF8.GetBytes(decryptedText);
    }

    public byte[] GenerateInitialIv()
    {
        var iv = new byte[IvSize];
        RandomNumberGenerator.Fill(iv);
        return iv;
    }
}