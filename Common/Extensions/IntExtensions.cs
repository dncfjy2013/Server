using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Common.Extensions
{
    /// <summary>
    /// 为 int 类型提供扩展方法的静态类。
    /// </summary>
    public static class IntExtensions
    {
        /// <summary>
        /// 判断一个整数是否为偶数。
        /// </summary>
        /// <param name="number">要检查的整数。</param>
        /// <returns>如果该整数是偶数，则返回 true；否则返回 false。</returns>
        public static bool IsEven(this int number)
        {
            return number % 2 == 0;
        }

        /// <summary>
        /// 判断一个整数是否为奇数。
        /// </summary>
        /// <param name="number">要检查的整数。</param>
        /// <returns>如果该整数是奇数，则返回 true；否则返回 false。</returns>
        public static bool IsOdd(this int number)
        {
            return number % 2 != 0;
        }

        /// <summary>
        /// 判断一个整数是否为质数。
        /// </summary>
        /// <param name="number">要检查的整数。</param>
        /// <returns>如果该整数是质数，则返回 true；否则返回 false。小于 2 的整数返回 false。</returns>
        public static bool IsPrime(this int number)
        {
            if (number < 2) return false;
            for (int i = 2; i <= Math.Sqrt(number); i++)
            {
                if (number % i == 0) return false;
            }
            return true;
        }

        /// <summary>
        /// 计算一个整数的阶乘。
        /// </summary>
        /// <param name="number">要计算阶乘的整数。</param>
        /// <returns>返回该整数的阶乘值。如果输入的整数为负数，则抛出 ArgumentException 异常。</returns>
        /// <exception cref="ArgumentException">当输入的整数为负数时抛出。</exception>
        public static long Factorial(this int number)
        {
            if (number < 0)
            {
                throw new ArgumentException("阶乘不能为负数");
            }
            long result = 1;
            for (int i = 2; i <= number; i++)
            {
                result *= i;
            }
            return result;
        }

        /// <summary>
        /// 生成从当前整数到指定上限的整数序列。
        /// </summary>
        /// <param name="start">序列的起始整数。</param>
        /// <param name="end">序列的结束整数。</param>
        /// <returns>返回一个包含从起始整数到结束整数的整数序列的 IEnumerable。</returns>
        public static IEnumerable<int> To(this int start, int end)
        {
            if (start <= end)
            {
                for (int i = start; i <= end; i++)
                {
                    yield return i;
                }
            }
            else
            {
                for (int i = start; i >= end; i--)
                {
                    yield return i;
                }
            }
        }

        /// <summary>
        /// 将一个整数转换为对应的罗马数字。
        /// </summary>
        /// <param name="number">要转换的整数。</param>
        /// <returns>返回表示该整数的罗马数字字符串。如果输入的整数不在 1 到 3999 的范围内，则抛出 ArgumentException 异常。</returns>
        /// <exception cref="ArgumentException">当输入的整数小于 1 或大于 3999 时抛出。</exception>
        public static string ToRomanNumeral(this int number)
        {
            if (number < 1 || number > 3999)
            {
                throw new ArgumentException("罗马数字只能表示 1 到 3999 之间的整数");
            }
            string[] thousands = { "", "M", "MM", "MMM" };
            string[] hundreds = { "", "C", "CC", "CCC", "CD", "D", "DC", "DCC", "DCCC", "CM" };
            string[] tens = { "", "X", "XX", "XXX", "XL", "L", "LX", "LXX", "LXXX", "XC" };
            string[] ones = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX" };

            return thousands[number / 1000] +
                   hundreds[(number % 1000) / 100] +
                   tens[(number % 100) / 10] +
                   ones[number % 10];
        }

        /// <summary>
        /// 计算一个整数的各位数字之和。
        /// </summary>
        /// <param name="number">要计算各位数字之和的整数。</param>
        /// <returns>返回该整数的各位数字之和，计算时会先取该整数的绝对值。</returns>
        public static int SumOfDigits(this int number)
        {
            number = Math.Abs(number);
            int sum = 0;
            while (number > 0)
            {
                sum += number % 10;
                number /= 10;
            }
            return sum;
        }

        /// <summary>
        /// 反转一个整数的各位数字。
        /// </summary>
        /// <param name="number">要反转各位数字的整数。</param>
        /// <returns>返回反转各位数字后的整数。如果原整数为负数，结果也为负数。</returns>
        public static int ReverseDigits(this int number)
        {
            int reversed = 0;
            bool isNegative = number < 0;
            number = Math.Abs(number);
            while (number > 0)
            {
                reversed = reversed * 10 + number % 10;
                number /= 10;
            }
            return isNegative ? -reversed : reversed;
        }

        /// <summary>
        /// 检查一个整数是否为回文数。
        /// </summary>
        /// <param name="number">要检查的整数。</param>
        /// <returns>如果该整数是回文数（即正序和倒序数字相同），则返回 true；否则返回 false。</returns>
        public static bool IsPalindrome(this int number)
        {
            return number == number.ReverseDigits();
        }

        /// <summary>
        /// 将一个整数转换为二进制字符串。
        /// </summary>
        /// <param name="number">要转换的整数。</param>
        /// <returns>返回表示该整数的二进制字符串。</returns>
        public static string ToBinary(this int number)
        {
            return Convert.ToString(number, 2);
        }

        /// <summary>
        /// 将一个整数转换为八进制字符串。
        /// </summary>
        /// <param name="number">要转换的整数。</param>
        /// <returns>返回表示该整数的八进制字符串。</returns>
        public static string ToOctal(this int number)
        {
            return Convert.ToString(number, 8);
        }

        /// <summary>
        /// 将一个整数转换为十六进制字符串，并且将结果转换为大写形式。
        /// </summary>
        /// <param name="number">要转换的整数。</param>
        /// <returns>返回表示该整数的十六进制大写字符串。</returns>
        public static string ToHexadecimal(this int number)
        {
            return Convert.ToString(number, 16).ToUpper();
        }

        /// <summary>
        /// 将指定进制的字符串转换为十进制整数。
        /// </summary>
        /// <param name="_">该参数仅为了使方法成为扩展方法而存在，实际无作用。</param>
        /// <param name="value">要转换的指定进制的字符串。</param>
        /// <param name="fromBase">字符串的进制数。</param>
        /// <returns>返回转换后的十进制整数。</returns>
        public static int FromBaseString(this int _, string value, int fromBase)
        {
            return Convert.ToInt32(value, fromBase);
        }
    }
}