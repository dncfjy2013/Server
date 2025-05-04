using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Common.Extensions
{
    /// <summary>
    /// 为 float 类型提供一系列扩展方法的静态类。
    /// </summary>
    public static class FloatExtensions
    {
        /// <summary>
        /// 判断单精度浮点数是否为整数。
        /// 通过比较该浮点数与其四舍五入后的结果差值的绝对值是否小于 float 类型的最小精度值来判断。
        /// </summary>
        /// <param name="number">要判断的单精度浮点数。</param>
        /// <returns>若为整数返回 true，否则返回 false。</returns>
        public static bool IsInteger(this float number)
        {
            return Math.Abs(number - (float)Math.Round(number)) < float.Epsilon;
        }

        /// <summary>
        /// 将单精度浮点数四舍五入到指定的小数位数。
        /// </summary>
        /// <param name="number">要进行四舍五入操作的单精度浮点数。</param>
        /// <param name="decimalPlaces">要保留的小数位数。</param>
        /// <returns>四舍五入后的单精度浮点数。</returns>
        public static float RoundTo(this float number, int decimalPlaces)
        {
            return (float)Math.Round(number, decimalPlaces);
        }

        /// <summary>
        /// 对单精度浮点数进行向上取整操作。
        /// </summary>
        /// <param name="number">要进行向上取整的单精度浮点数。</param>
        /// <returns>向上取整后的单精度浮点数。</returns>
        public static float Ceiling(this float number)
        {
            return (float)Math.Ceiling(number);
        }

        /// <summary>
        /// 对单精度浮点数进行向下取整操作。
        /// </summary>
        /// <param name="number">要进行向下取整的单精度浮点数。</param>
        /// <returns>向下取整后的单精度浮点数。</returns>
        public static float Floor(this float number)
        {
            return (float)Math.Floor(number);
        }

        /// <summary>
        /// 计算单精度浮点数的绝对值。
        /// </summary>
        /// <param name="number">要计算绝对值的单精度浮点数。</param>
        /// <returns>该浮点数的绝对值。</returns>
        public static float Abs(this float number)
        {
            return Math.Abs(number);
        }

        /// <summary>
        /// 计算单精度浮点数的平方。
        /// </summary>
        /// <param name="number">要计算平方的单精度浮点数。</param>
        /// <returns>该浮点数的平方值。</returns>
        public static float Square(this float number)
        {
            return number * number;
        }

        /// <summary>
        /// 计算单精度浮点数的平方根。
        /// 若传入的数为负数，会抛出 ArgumentException 异常。
        /// </summary>
        /// <param name="number">要计算平方根的单精度浮点数。</param>
        /// <returns>该浮点数的平方根。</returns>
        /// <exception cref="ArgumentException">当传入的数为负数时抛出。</exception>
        public static float Sqrt(this float number)
        {
            if (number < 0)
            {
                throw new ArgumentException("不能对负数求平方根");
            }
            return (float)Math.Sqrt(number);
        }

        /// <summary>
        /// 计算单精度浮点数的指定次幂。
        /// </summary>
        /// <param name="number">底数，即要进行幂运算的单精度浮点数。</param>
        /// <param name="power">指数，即指定的幂次。</param>
        /// <returns>底数的指定次幂的结果。</returns>
        public static float Pow(this float number, float power)
        {
            return (float)Math.Pow(number, power);
        }

        /// <summary>
        /// 判断两个单精度浮点数是否在指定的容差范围内近似相等。
        /// </summary>
        /// <param name="number1">第一个要比较的单精度浮点数。</param>
        /// <param name="number2">第二个要比较的单精度浮点数。</param>
        /// <param name="tolerance">容差范围，默认为 float 类型的最小精度值。</param>
        /// <returns>若两个数的差值绝对值小于容差则返回 true，否则返回 false。</returns>
        public static bool IsApproximatelyEqual(this float number1, float number2, float tolerance = float.Epsilon)
        {
            return Math.Abs(number1 - number2) < tolerance;
        }

        /// <summary>
        /// 将单精度浮点数转换为百分比字符串。
        /// </summary>
        /// <param name="number">要转换的单精度浮点数。</param>
        /// <param name="decimalPlaces">转换后百分比字符串要保留的小数位数，默认为 2 位。</param>
        /// <returns>转换后的百分比字符串。</returns>
        public static string ToPercentageString(this float number, int decimalPlaces = 2)
        {
            return (number * 100).ToString($"F{decimalPlaces}") + "%";
        }

        /// <summary>
        /// 检查单精度浮点数是否为非数字（NaN）。
        /// </summary>
        /// <param name="number">要检查的单精度浮点数。</param>
        /// <returns>若为 NaN 则返回 true，否则返回 false。</returns>
        public static bool IsNaN(this float number)
        {
            return float.IsNaN(number);
        }

        /// <summary>
        /// 检查单精度浮点数是否为正无穷大。
        /// </summary>
        /// <param name="number">要检查的单精度浮点数。</param>
        /// <returns>若为正无穷大则返回 true，否则返回 false。</returns>
        public static bool IsPositiveInfinity(this float number)
        {
            return float.IsPositiveInfinity(number);
        }

        /// <summary>
        /// 检查单精度浮点数是否为负无穷大。
        /// </summary>
        /// <param name="number">要检查的单精度浮点数。</param>
        /// <returns>若为负无穷大则返回 true，否则返回 false。</returns>
        public static bool IsNegativeInfinity(this float number)
        {
            return float.IsNegativeInfinity(number);
        }

        /// <summary>
        /// 将单精度浮点数转换为双精度浮点数。
        /// </summary>
        /// <param name="number">要转换的单精度浮点数。</param>
        /// <returns>转换后的双精度浮点数。</returns>
        public static double ToDouble(this float number)
        {
            return Convert.ToDouble(number);
        }

        /// <summary>
        /// 将单精度浮点数转换为整数，直接截断小数部分。
        /// </summary>
        /// <param name="number">要转换的单精度浮点数。</param>
        /// <returns>转换后的整数。</returns>
        public static int ToInt(this float number)
        {
            return (int)number;
        }
    }
}