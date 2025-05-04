using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Common.Extensions
{
    /// <summary>
    /// 为 double 类型提供扩展方法的静态类。
    /// </summary>
    public static class DoubleExtensions
    {
        /// <summary>
        /// 判断双精度浮点数是否为整数。
        /// 此方法通过比较该数与其四舍五入后的值的差的绝对值是否小于 double 类型的最小精度值来判断。
        /// </summary>
        /// <param name="number">要进行判断的双精度浮点数。</param>
        /// <returns>如果该数为整数，返回 true；否则返回 false。</returns>
        public static bool IsInteger(this double number)
        {
            return Math.Abs(number - Math.Round(number)) < double.Epsilon;
        }

        /// <summary>
        /// 将双精度浮点数四舍五入到指定的小数位数。
        /// </summary>
        /// <param name="number">要进行四舍五入操作的双精度浮点数。</param>
        /// <param name="decimalPlaces">要保留的小数位数。</param>
        /// <returns>四舍五入后的双精度浮点数。</returns>
        public static double RoundTo(this double number, int decimalPlaces)
        {
            return Math.Round(number, decimalPlaces);
        }

        /// <summary>
        /// 对双精度浮点数进行向上取整操作。
        /// </summary>
        /// <param name="number">要进行向上取整的双精度浮点数。</param>
        /// <returns>向上取整后的双精度浮点数。</returns>
        public static double Ceiling(this double number)
        {
            return Math.Ceiling(number);
        }

        /// <summary>
        /// 对双精度浮点数进行向下取整操作。
        /// </summary>
        /// <param name="number">要进行向下取整的双精度浮点数。</param>
        /// <returns>向下取整后的双精度浮点数。</returns>
        public static double Floor(this double number)
        {
            return Math.Floor(number);
        }

        /// <summary>
        /// 计算双精度浮点数的绝对值。
        /// </summary>
        /// <param name="number">要计算绝对值的双精度浮点数。</param>
        /// <returns>该数的绝对值。</returns>
        public static double Abs(this double number)
        {
            return Math.Abs(number);
        }

        /// <summary>
        /// 计算双精度浮点数的平方。
        /// </summary>
        /// <param name="number">要计算平方的双精度浮点数。</param>
        /// <returns>该数的平方值。</returns>
        public static double Square(this double number)
        {
            return number * number;
        }

        /// <summary>
        /// 计算双精度浮点数的平方根。
        /// 如果传入的数为负数，将抛出 ArgumentException 异常。
        /// </summary>
        /// <param name="number">要计算平方根的双精度浮点数。</param>
        /// <returns>该数的平方根。</returns>
        /// <exception cref="ArgumentException">当传入的数为负数时抛出。</exception>
        public static double Sqrt(this double number)
        {
            if (number < 0)
            {
                throw new ArgumentException("不能对负数求平方根");
            }
            return Math.Sqrt(number);
        }

        /// <summary>
        /// 计算双精度浮点数的指定次幂。
        /// </summary>
        /// <param name="number">底数，即要进行幂运算的双精度浮点数。</param>
        /// <param name="power">指数，即指定的幂次。</param>
        /// <returns>底数的指定次幂的结果。</returns>
        public static double Pow(this double number, double power)
        {
            return Math.Pow(number, power);
        }

        /// <summary>
        /// 判断两个双精度浮点数是否在指定的容差范围内近似相等。
        /// </summary>
        /// <param name="number1">第一个要比较的双精度浮点数。</param>
        /// <param name="number2">第二个要比较的双精度浮点数。</param>
        /// <param name="tolerance">容差范围，默认为 double 类型的最小精度值。</param>
        /// <returns>如果两个数的差的绝对值小于容差，返回 true；否则返回 false。</returns>
        public static bool IsApproximatelyEqual(this double number1, double number2, double tolerance = double.Epsilon)
        {
            return Math.Abs(number1 - number2) < tolerance;
        }

        /// <summary>
        /// 将双精度浮点数转换为百分比字符串。
        /// </summary>
        /// <param name="number">要转换的双精度浮点数。</param>
        /// <param name="decimalPlaces">转换后百分比字符串要保留的小数位数，默认为 2 位。</param>
        /// <returns>转换后的百分比字符串。</returns>
        public static string ToPercentageString(this double number, int decimalPlaces = 2)
        {
            return (number * 100).ToString($"F{decimalPlaces}") + "%";
        }

        /// <summary>
        /// 检查双精度浮点数是否为非数字（NaN）。
        /// </summary>
        /// <param name="number">要检查的双精度浮点数。</param>
        /// <returns>如果该数为 NaN，返回 true；否则返回 false。</returns>
        public static bool IsNaN(this double number)
        {
            return double.IsNaN(number);
        }

        /// <summary>
        /// 检查双精度浮点数是否为正无穷大。
        /// </summary>
        /// <param name="number">要检查的双精度浮点数。</param>
        /// <returns>如果该数为正无穷大，返回 true；否则返回 false。</returns>
        public static bool IsPositiveInfinity(this double number)
        {
            return double.IsPositiveInfinity(number);
        }

        /// <summary>
        /// 检查双精度浮点数是否为负无穷大。
        /// </summary>
        /// <param name="number">要检查的双精度浮点数。</param>
        /// <returns>如果该数为负无穷大，返回 true；否则返回 false。</returns>
        public static bool IsNegativeInfinity(this double number)
        {
            return double.IsNegativeInfinity(number);
        }
    }
}