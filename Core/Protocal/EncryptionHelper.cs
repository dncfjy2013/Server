using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Core.Protocal
{
    public static class EncryptionHelper
    {
        private static readonly byte[] Key = GetSecureSafeKey();
        private const int KeySize = 256;
        private const int BlockSize = 128; // 16字节
        private const int HmacSize = 32;
        private const int IvSizeGcm = 12; // AES-GCM推荐使用12字节IV
        private const int IvSizeCbc = 16; // AES-CBC需要16字节IV，与块大小一致
        private const int TagSize = 16; // AES-GCM标签大小(128位)

        // 使用线程静态字段替代ThreadLocal，确保每个线程有独立实例
        [ThreadStatic] private static AesGcm _aesGcm;
        [ThreadStatic] private static HMACSHA256 _hmac;

        // 安全的密钥生成方法 - 在实际应用中应使用更安全的密钥管理方案
        private static byte[] GetSecureKey()
        {
            // 使用32字节(256位)的密钥
            string keyString = "YourSecureKey12345678901234567890"; // 正好32字节

            // 截取或填充密钥到正确长度
            byte[] keyBytes = new byte[32]; // 256位密钥
            byte[] inputBytes = Encoding.UTF8.GetBytes(keyString);

            // 复制输入字节到密钥数组，如果输入不足32字节则剩余部分为0
            Array.Copy(inputBytes, keyBytes, Math.Min(inputBytes.Length, keyBytes.Length));

            return keyBytes;
        }
        // 安全的密钥生成方法 - 使用时间作为种子的随机密钥生成
        private static byte[] GetSecureSafeKey()
        {
            // 256位密钥(32字节)
            byte[] keyBytes = new byte[32];

            // 使用当前时间作为随机种子
            long timestamp = DateTime.UtcNow.Ticks;
            byte[] timestampBytes = BitConverter.GetBytes(timestamp);

            // 额外的熵数据，可以是任何固定或动态数据
            byte[] entropy = Encoding.UTF8.GetBytes("EncryptionEntropyData2025");

            // 组合种子和熵
            using (var ms = new MemoryStream())
            {
                ms.Write(timestampBytes, 0, timestampBytes.Length);
                ms.Write(entropy, 0, entropy.Length);
                ms.Write(BitConverter.GetBytes(Guid.NewGuid().GetHashCode()), 0, 4);

                // 使用HMACSHA256生成扩展的密钥材料
                using (var hmac = new HMACSHA256())
                {
                    byte[] keyMaterial = ms.ToArray();
                    hmac.Key = keyMaterial;
                    byte[] hashedKey = hmac.ComputeHash(keyMaterial);

                    // 确保生成32字节的密钥
                    Array.Copy(hashedKey, keyBytes, Math.Min(hashedKey.Length, keyBytes.Length));

                    // 如果需要，用加密安全的随机数填充剩余部分
                    if (hashedKey.Length < keyBytes.Length)
                    {
                        using (var rng = new RNGCryptoServiceProvider())
                        {
                            rng.GetBytes(keyBytes, hashedKey.Length, keyBytes.Length - hashedKey.Length);
                        }
                    }
                }
            }

            return keyBytes;
        }

        // 获取当前线程的AesGcm实例
        private static AesGcm GetAesGcm() => _aesGcm ??= new AesGcm(Key);

        // 获取当前线程的HMAC实例
        private static HMACSHA256 GetHmac() => _hmac ??= new HMACSHA256(Key);

        public static byte[] GenerateIVGcm()
        {
            var iv = new byte[IvSizeGcm];
            RandomNumberGenerator.Fill(iv);
            return iv;
        }

        public static byte[] GenerateIVCbc()
        {
            var iv = new byte[IvSizeCbc];
            RandomNumberGenerator.Fill(iv);
            return iv;
        }

        /// <summary>
        /// 使用AES-GCM算法加密文本
        /// </summary>
        /// <param name="plainText">待加密的明文</param>
        /// <returns>包含IV、认证标签和密文的字节数组</returns>
        public static async Task<byte[]> EncryptAsync(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                throw new ArgumentNullException(nameof(plainText));

            var plainTextBytes = Encoding.UTF8.GetBytes(plainText);
            var iv = GenerateIVGcm();
            var cipherText = new byte[plainTextBytes.Length];
            var tag = new byte[TagSize];

            try
            {
                GetAesGcm().Encrypt(iv, plainTextBytes, cipherText, tag);

                var outputLength = IvSizeGcm + TagSize + plainTextBytes.Length;
                var output = new byte[outputLength];

                Buffer.BlockCopy(iv, 0, output, 0, IvSizeGcm);
                Buffer.BlockCopy(tag, 0, output, IvSizeGcm, TagSize);
                Buffer.BlockCopy(cipherText, 0, output, IvSizeGcm + TagSize, plainTextBytes.Length);

                return output;
            }
            finally
            {
                // 安全清除敏感数据
                Array.Clear(cipherText, 0, cipherText.Length);
                Array.Clear(tag, 0, tag.Length);
                Array.Clear(plainTextBytes, 0, plainTextBytes.Length);
            }
        }

        /// <summary>
        /// 使用AES-GCM算法解密数据
        /// </summary>
        /// <param name="cipherText">包含IV、认证标签和密文的字节数组</param>
        /// <returns>解密后的明文</returns>
        public static async Task<string> DecryptAsync(byte[] cipherText)
        {
            if (cipherText == null || cipherText.Length < IvSizeGcm + TagSize)
                throw new ArgumentException("无效的密文格式", nameof(cipherText));

            var iv = new byte[IvSizeGcm];
            var tag = new byte[TagSize];
            var encryptedData = new byte[cipherText.Length - IvSizeGcm - TagSize];

            try
            {
                Buffer.BlockCopy(cipherText, 0, iv, 0, IvSizeGcm);
                Buffer.BlockCopy(cipherText, IvSizeGcm, tag, 0, TagSize);
                Buffer.BlockCopy(cipherText, IvSizeGcm + TagSize, encryptedData, 0, encryptedData.Length);

                var decryptedData = new byte[encryptedData.Length];
                GetAesGcm().Decrypt(iv, encryptedData, tag, decryptedData);

                return Encoding.UTF8.GetString(decryptedData);
            }
            finally
            {
                // 安全清除敏感数据
                Array.Clear(iv, 0, iv.Length);
                Array.Clear(tag, 0, tag.Length);
                Array.Clear(encryptedData, 0, encryptedData.Length);
            }
        }

        /// <summary>
        /// 使用混合加密方法加密文本（兼容旧版.NET，使用AES-CBC+HMAC）
        /// </summary>
        /// <param name="plainText">待加密的明文</param>
        /// <returns>包含IV、密文和HMAC的字节数组</returns>
        public static async Task<byte[]> EncryptHybridAsync(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                throw new ArgumentNullException(nameof(plainText));

            using var aes = Aes.Create();
            aes.KeySize = KeySize;
            aes.BlockSize = BlockSize;
            aes.Key = Key;
            aes.IV = GenerateIVCbc(); // 使用16字节IV
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            var plainTextBytes = Encoding.UTF8.GetBytes(plainText);

            // 使用MemoryStream来获取实际加密后的长度
            using var ms = new MemoryStream();
            ms.Write(aes.IV, 0, IvSizeCbc); // 写入完整的16字节IV

            using (var encryptor = aes.CreateEncryptor())
            using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
            {
                await cs.WriteAsync(plainTextBytes, 0, plainTextBytes.Length);
                await cs.FlushFinalBlockAsync();
            }

            var encryptedData = ms.ToArray();
            var encryptedDataLength = encryptedData.Length - IvSizeCbc;

            // 创建最终缓冲区: IV + 加密数据 + HMAC
            var buffer = new byte[IvSizeCbc + encryptedDataLength + HmacSize];

            // 复制IV和加密数据
            Buffer.BlockCopy(encryptedData, 0, buffer, 0, IvSizeCbc + encryptedDataLength);

            // 计算并添加HMAC
            var hmac = GetHmac().ComputeHash(buffer, 0, IvSizeCbc + encryptedDataLength);
            Buffer.BlockCopy(hmac, 0, buffer, IvSizeCbc + encryptedDataLength, HmacSize);

            return buffer;
        }

        /// <summary>
        /// 使用混合加密方法解密数据（兼容旧版.NET，使用AES-CBC+HMAC）
        /// </summary>
        /// <param name="cipherText">包含IV、密文和HMAC的字节数组</param>
        /// <returns>解密后的明文</returns>
        public static async Task<string> DecryptHybridAsync(byte[] cipherText)
        {
            if (cipherText == null || cipherText.Length < IvSizeCbc + HmacSize)
                throw new ArgumentException("无效的密文格式", nameof(cipherText));

            // 提取IV、密文和HMAC
            var iv = new byte[IvSizeCbc];
            var hmac = new byte[HmacSize];
            var encryptedDataLength = cipherText.Length - IvSizeCbc - HmacSize;
            var encryptedData = new byte[encryptedDataLength];

            try
            {
                // 从缓冲区复制数据
                Buffer.BlockCopy(cipherText, 0, iv, 0, IvSizeCbc);
                Buffer.BlockCopy(cipherText, IvSizeCbc + encryptedDataLength, hmac, 0, HmacSize);
                Buffer.BlockCopy(cipherText, IvSizeCbc, encryptedData, 0, encryptedDataLength);

                // 验证HMAC - 使用与加密时相同的范围
                var computedHmac = GetHmac().ComputeHash(cipherText, 0, IvSizeCbc + encryptedDataLength);
                if (!CryptographicOperations.FixedTimeEquals(computedHmac, hmac))
                    throw new CryptographicException("HMAC验证失败，数据可能已被篡改");

                // 使用AES解密
                using var aes = Aes.Create();
                aes.KeySize = KeySize;
                aes.BlockSize = BlockSize;
                aes.Key = Key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using var ms = new MemoryStream(encryptedData);
                using var decryptor = aes.CreateDecryptor();
                using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
                using var sr = new StreamReader(cs);

                return await sr.ReadToEndAsync();
            }
            finally
            {
                // 安全清除敏感数据
                Array.Clear(iv, 0, iv.Length);
                Array.Clear(hmac, 0, hmac.Length);
                Array.Clear(encryptedData, 0, encryptedData.Length);
            }
        }

        /// <summary>
        /// 计算数据的HMAC值
        /// </summary>
        /// <param name="data">待计算HMAC的数据</param>
        /// <returns>HMAC值</returns>
        public static byte[] ComputeHMAC(ReadOnlySpan<byte> data)
        {
            var hmac = new byte[HmacSize];
            GetHmac().TryComputeHash(data, hmac, out _);
            return hmac;
        }

        /// <summary>
        /// 验证数据的HMAC值
        /// </summary>
        /// <param name="data">待验证的数据</param>
        /// <param name="hmac">待验证的HMAC值</param>
        /// <returns>验证结果</returns>
        public static bool VerifyHMAC(ReadOnlySpan<byte> data, ReadOnlySpan<byte> hmac)
        {
            Span<byte> computedHmac = stackalloc byte[HmacSize];
            GetHmac().TryComputeHash(data, computedHmac, out _);
            return CryptographicOperations.FixedTimeEquals(computedHmac, hmac);
        }
    }
}