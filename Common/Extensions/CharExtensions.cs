using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Server.Common.Extensions
{
    /// <summary>
    /// 为 char 类型提供扩展方法的静态类。
    /// </summary>
    public static class CharExtensions
    {
        /// <summary>
        /// 判断指定字符是否为大写字母。
        /// </summary>
        /// <param name="c">要进行判断的字符。</param>
        /// <returns>如果字符是大写字母，返回 true；否则返回 false。</returns>
        public static bool IsUpperCase(this char c)
        {
            return char.IsUpper(c);
        }

        /// <summary>
        /// 判断指定字符是否为小写字母。
        /// </summary>
        /// <param name="c">要进行判断的字符。</param>
        /// <returns>如果字符是小写字母，返回 true；否则返回 false。</returns>
        public static bool IsLowerCase(this char c)
        {
            return char.IsLower(c);
        }

        /// <summary>
        /// 判断指定字符是否为数字。
        /// </summary>
        /// <param name="c">要进行判断的字符。</param>
        /// <returns>如果字符是数字，返回 true；否则返回 false。</returns>
        public static bool IsDigit(this char c)
        {
            return char.IsDigit(c);
        }

        /// <summary>
        /// 判断指定字符是否为字母。
        /// </summary>
        /// <param name="c">要进行判断的字符。</param>
        /// <returns>如果字符是字母，返回 true；否则返回 false。</returns>
        public static bool IsLetter(this char c)
        {
            return char.IsLetter(c);
        }

        /// <summary>
        /// 判断指定字符是否为字母或数字。
        /// </summary>
        /// <param name="c">要进行判断的字符。</param>
        /// <returns>如果字符是字母或数字，返回 true；否则返回 false。</returns>
        public static bool IsLetterOrDigit(this char c)
        {
            return char.IsLetterOrDigit(c);
        }

        /// <summary>
        /// 判断指定字符是否为空白字符。
        /// </summary>
        /// <param name="c">要进行判断的字符。</param>
        /// <returns>如果字符是空白字符，返回 true；否则返回 false。</returns>
        public static bool IsWhiteSpace(this char c)
        {
            return char.IsWhiteSpace(c);
        }

        /// <summary>
        /// 将指定字符转换为大写形式。
        /// </summary>
        /// <param name="c">要进行转换的字符。</param>
        /// <returns>转换后的大写字符。</returns>
        public static char ToUpper(this char c)
        {
            return char.ToUpper(c);
        }

        /// <summary>
        /// 将指定字符转换为小写形式。
        /// </summary>
        /// <param name="c">要进行转换的字符。</param>
        /// <returns>转换后的小写字符。</returns>
        public static char ToLower(this char c)
        {
            return char.ToLower(c);
        }

        /// <summary>
        /// 判断指定字符是否为标点符号。
        /// </summary>
        /// <param name="c">要进行判断的字符。</param>
        /// <returns>如果字符是标点符号，返回 true；否则返回 false。</returns>
        public static bool IsPunctuation(this char c)
        {
            return char.IsPunctuation(c);
        }

        /// <summary>
        /// 判断指定字符是否为控制字符。
        /// </summary>
        /// <param name="c">要进行判断的字符。</param>
        /// <returns>如果字符是控制字符，返回 true；否则返回 false。</returns>
        public static bool IsControl(this char c)
        {
            return char.IsControl(c);
        }

        /// <summary>
        /// 判断指定字符是否为 Unicode 中的符号。
        /// </summary>
        /// <param name="c">要进行判断的字符。</param>
        /// <returns>如果字符是 Unicode 中的符号，返回 true；否则返回 false。</returns>
        public static bool IsSymbol(this char c)
        {
            return char.IsSymbol(c);
        }

        /// <summary>
        /// 将指定字符转换为对应的 ASCII 码值。
        /// </summary>
        /// <param name="c">要进行转换的字符。</param>
        /// <returns>字符对应的 ASCII 码值。</returns>
        public static int ToAsciiCode(this char c)
        {
            return Convert.ToInt32(c);
        }

        /// <summary>
        /// 判断指定字符是否为元音字母（不区分大小写）。
        /// </summary>
        /// <param name="c">要进行判断的字符。</param>
        /// <returns>如果字符是元音字母，返回 true；否则返回 false。</returns>
        public static bool IsVowel(this char c)
        {
            char lowerC = char.ToLower(c);
            return lowerC == 'a' || lowerC == 'e' || lowerC == 'i' || lowerC == 'o' || lowerC == 'u';
        }

        /// <summary>
        /// 判断指定字符是否为十六进制数字。
        /// </summary>
        /// <param name="c">要进行判断的字符。</param>
        /// <returns>如果字符是十六进制数字，返回 true；否则返回 false。</returns>
        public static bool IsHexDigit(this char c)
        {
            return Regex.IsMatch(c.ToString(), @"^[0-9a-fA-F]$");
        }
    }
}