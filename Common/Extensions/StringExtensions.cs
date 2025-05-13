using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Server.Common.Extensions
{
    public static class StringExtensions
    {
        #region 空值检查
        /// <summary>
        /// 检查字符串是否为null或空字符串
        /// </summary>
        public static bool IsNullOrEmpty(this string? value) => string.IsNullOrEmpty(value);

        /// <summary>
        /// 检查字符串是否为null、空字符串或仅包含空白字符
        /// </summary>
        public static bool IsNullOrWhiteSpace(this string? value) => string.IsNullOrWhiteSpace(value);
        #endregion

        #region 格式转换
        /// <summary>
        /// 将字符串居中填充到指定长度，使用指定字符进行填充
        /// </summary>
        /// <param name="source">源字符串</param>
        /// <param name="totalWidth">扩展后的总长度</param>
        /// <param name="paddingChar">填充字符，默认为空格</param>
        /// <returns>居中后的字符串</returns>
        public static string Center(this string source, int totalWidth, char paddingChar = ' ')
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            if (totalWidth < 0)
                throw new ArgumentException("总宽度不能为负数", nameof(totalWidth));

            if (source.Length >= totalWidth)
                return source;

            int leftPadding = (totalWidth - source.Length) / 2;
            int rightPadding = totalWidth - source.Length - leftPadding;

            return new string(paddingChar, leftPadding) + source + new string(paddingChar, rightPadding);
        }

        /// <summary>
        /// 将字符串居中填充到指定长度，使用指定字符串进行填充
        /// </summary>
        /// <param name="source">源字符串</param>
        /// <param name="totalWidth">扩展后的总长度</param>
        /// <param name="paddingString">填充字符串，默认为空格</param>
        /// <returns>居中后的字符串</returns>
        public static string Center(this string source, int totalWidth, string paddingString = " ")
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            if (paddingString == null)
                throw new ArgumentNullException(nameof(paddingString));

            if (paddingString.Length == 0)
                throw new ArgumentException("填充字符串不能为空", nameof(paddingString));

            if (totalWidth < 0)
                throw new ArgumentException("总宽度不能为负数", nameof(totalWidth));

            if (source.Length >= totalWidth)
                return source;

            int paddingLength = totalWidth - source.Length;
            int leftPaddingLength = paddingLength / 2;
            int rightPaddingLength = paddingLength - leftPaddingLength;

            string leftPadding = GeneratePadding(paddingString, leftPaddingLength);
            string rightPadding = GeneratePadding(paddingString, rightPaddingLength);

            return leftPadding + source + rightPadding;
        }

        private static string GeneratePadding(string paddingString, int length)
        {
            if (length <= 0)
                return string.Empty;

            int repeatCount = length / paddingString.Length;
            int remainder = length % paddingString.Length;

            return new String(paddingString[0], repeatCount) + paddingString.Substring(0, remainder);
        }
        /// <summary>
        /// 将字符串转换为驼峰命名法（首字母小写）
        /// </summary>
        public static string ToCamelCase(this string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return char.ToLowerInvariant(value[0]) + value.Substring(1);
        }

        /// <summary>
        /// 将下划线命名转换为驼峰命名
        /// </summary>
        public static string SnakeCaseToCamelCase(this string value)
        {
            return string.Join("", value.Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries)
                .Select((s, i) => i == 0
                    ? s.ToLowerInvariant()
                    : CultureInfo.CurrentCulture.TextInfo.ToTitleCase(s)));
        }

        /// <summary>
        /// 转换为首字母大写形式
        /// </summary>
        public static string ToTitleCase(this string value)
        {
            return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(value.ToLower());
        }

        /// <summary>
        /// 将十六进制字符串转换为字节数组
        /// </summary>
        public static byte[] HexToBytes(this string hex)
        {
            if (string.IsNullOrEmpty(hex))
            {
                return new byte[0];
            }
            if (hex.Length % 2 != 0)
            {
                throw new ArgumentException("十六进制字符串的长度必须是偶数。");
            }
            return Enumerable.Range(0, hex.Length)
                             .Where(x => x % 2 == 0)
                             .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                             .ToArray();
        }

        /// <summary>
        /// 将字节数组转换为十六进制字符串
        /// </summary>
        public static string BytesToHex(this byte[] bytes)
        {
            if (bytes == null)
            {
                return string.Empty;
            }
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>
        /// 将十六进制字符串转换为整数
        /// </summary>
        public static int HexToInt(this string hex)
        {
            return Convert.ToInt32(hex, 16);
        }
        #endregion

        #region 安全处理
        /// <summary>
        /// 计算字符串的MD5哈希值
        /// </summary>
        public static string ToMD5(this string input)
        {
            using var md5 = MD5.Create();
            byte[] inputBytes = Encoding.UTF8.GetBytes(input);
            byte[] hashBytes = md5.ComputeHash(inputBytes);

            var sb = new StringBuilder();
            foreach (byte b in hashBytes)
            {
                sb.Append(b.ToString("x2"));
            }
            return sb.ToString();
        }

        /// <summary>
        /// 计算字符串的SHA256哈希值
        /// </summary>
        public static string ToSHA256(this string input)
        {
            using var sha256 = SHA256.Create();
            byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));

            var builder = new StringBuilder();
            foreach (byte b in bytes)
            {
                builder.Append(b.ToString("x2"));
            }
            return builder.ToString();
        }

        /// <summary>
        /// AES加密字符串
        /// </summary>
        public static string EncryptAES(this string plainText, string key, string iv)
        {
            using Aes aesAlg = Aes.Create();
            aesAlg.Key = Encoding.UTF8.GetBytes(key.PadRight(32));
            aesAlg.IV = Encoding.UTF8.GetBytes(iv.PadRight(16));

            ICryptoTransform encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);

            using var msEncrypt = new MemoryStream();
            using var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write);
            using (var swEncrypt = new StreamWriter(csEncrypt))
            {
                swEncrypt.Write(plainText);
            }
            return Convert.ToBase64String(msEncrypt.ToArray());
        }

        /// <summary>
        /// AES解密字符串
        /// </summary>
        public static string DecryptAES(this string cipherText, string key, string iv)
        {
            using Aes aesAlg = Aes.Create();
            aesAlg.Key = Encoding.UTF8.GetBytes(key.PadRight(32));
            aesAlg.IV = Encoding.UTF8.GetBytes(iv.PadRight(16));

            ICryptoTransform decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV);

            using var msDecrypt = new MemoryStream(Convert.FromBase64String(cipherText));
            using var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read);
            using var srDecrypt = new StreamReader(csDecrypt);
            return srDecrypt.ReadToEnd();
        }
        #endregion

        #region 验证方法
        /// <summary>
        /// 验证是否为有效邮箱地址
        /// </summary>
        public static bool IsValidEmail(this string email)
        {
            return Regex.IsMatch(email,
                @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
                RegexOptions.IgnoreCase);
        }

        /// <summary>
        /// 验证是否为有效URL
        /// </summary>
        public static bool IsValidUrl(this string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uriResult)
                && (uriResult.Scheme == Uri.UriSchemeHttp
                    || uriResult.Scheme == Uri.UriSchemeHttps);
        }

        /// <summary>
        /// 验证是否为有效手机号码（中国）
        /// </summary>
        public static bool IsValidChineseMobile(this string mobile)
        {
            return Regex.IsMatch(mobile, @"^1[3-9]\d{9}$");
        }
        #endregion

        #region 字符串处理
        /// <summary>
        /// 安全截断字符串
        /// </summary>
        public static string Truncate(this string value, int maxLength, string suffix = "...")
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Length <= maxLength
                ? value
                : value.Substring(0, maxLength) + suffix;
        }

        /// <summary>
        /// 生成指定长度的随机字符串
        /// </summary>
        public static string RandomString(int length, bool useSpecialChars = false)
        {
            const string validChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890";
            const string specialChars = "!@#$%^&*()_-+=[{]};:<>|./?";

            var chars = useSpecialChars
                ? validChars + specialChars
                : validChars;

            var data = new byte[4 * length];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(data);

            var result = new StringBuilder(length);
            for (int i = 0; i < length; i++)
            {
                var rnd = BitConverter.ToUInt32(data, i * 4);
                var idx = rnd % chars.Length;
                result.Append(chars[(int)idx]);
            }
            return result.ToString();
        }

        /// <summary>
        /// 移除HTML标签
        /// </summary>
        public static string StripHtmlTags(this string input)
        {
            return Regex.Replace(input, "<.*?>", string.Empty);
        }

        /// <summary>
        /// 转换为安全SQL字符串
        /// </summary>
        public static string ToSafeSqlString(this string input)
        {
            return input.Replace("'", "''");
        }
        #endregion

        #region 编码转换
        /// <summary>
        /// Base64编码
        /// </summary>
        public static string ToBase64(this string input)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(input));
        }

        /// <summary>
        /// Base64解码
        /// </summary>
        public static string FromBase64(this string input)
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(input));
        }

        /// <summary>
        /// URL安全Base64编码
        /// </summary>
        public static string ToUrlSafeBase64(this string input)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(input))
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
        }

        /// <summary>
        /// URL安全Base64解码
        /// </summary>
        public static string FromUrlSafeBase64(this string input)
        {
            string base64 = input.Replace('-', '+').Replace('_', '/');
            switch (input.Length % 4)
            {
                case 2: base64 += "=="; break;
                case 3: base64 += "="; break;
            }
            return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        }
        #endregion

        #region 其他实用方法
        /// <summary>
        /// 统计单词数量
        /// </summary>
        public static int WordCount(this string input)
        {
            return Regex.Matches(input, @"\b[\w']+\b").Count;
        }

        /// <summary>
        /// 判断字符串是否包含中文字符
        /// </summary>
        public static bool ContainsChinese(this string input)
        {
            return Regex.IsMatch(input, @"[\u4e00-\u9fa5]");
        }

        /// <summary>
        /// 转换为JSON安全字符串（转义特殊字符）
        /// </summary>
        public static string ToJsonSafeString(this string input)
        {
            return input.Replace("\\", "\\\\")
                       .Replace("\"", "\\\"")
                       .Replace("\n", "\\n")
                       .Replace("\r", "\\r")
                       .Replace("\t", "\\t");
        }
        #endregion
    }
}
