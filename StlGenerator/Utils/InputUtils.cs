using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StlGenerator.Utils
{
    /// <summary>
    /// 输入处理工具类，提供用户输入验证和转换功能
    /// </summary>
    public static class InputUtils
    {
        /// <summary>
        /// 获取用户输入的字符串，支持默认值
        /// </summary>
        public static string GetStringInput(string prompt, string defaultValue = "")
        {
            Console.Write(prompt);
            string input = Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(input))
                return defaultValue;

            return input;
        }

        /// <summary>
        /// 获取用户输入的整数，支持默认值和范围限制
        /// </summary>
        public static int GetIntegerInput(string prompt, int defaultValue, int minValue = int.MinValue, int maxValue = int.MaxValue)
        {
            while (true)
            {
                Console.Write(prompt);
                string input = Console.ReadLine()?.Trim();

                if (string.IsNullOrEmpty(input))
                    return defaultValue;

                if (int.TryParse(input, out int result) && result >= minValue && result <= maxValue)
                    return result;

                Console.WriteLine($"请输入一个介于 {minValue} 和 {maxValue} 之间的整数!");
            }
        }

        /// <summary>
        /// 获取用户输入的浮点数，支持默认值和范围限制
        /// </summary>
        public static float GetFloatInput(string prompt, float defaultValue, float minValue = float.MinValue, float maxValue = float.MaxValue)
        {
            while (true)
            {
                Console.Write(prompt);
                string input = Console.ReadLine()?.Trim();

                if (string.IsNullOrEmpty(input))
                    return defaultValue;

                if (float.TryParse(input, out float result) && result >= minValue && result <= maxValue)
                    return result;

                Console.WriteLine($"请输入一个介于 {minValue} 和 {maxValue} 之间的数字!");
            }
        }

        /// <summary>
        /// 获取用户输入的布尔值(Y/N)，支持默认值
        /// </summary>
        public static bool GetYesNoInput(string prompt, bool defaultValue)
        {
            string defaultStr = defaultValue ? "[Y/n]" : "[y/N]";

            while (true)
            {
                Console.Write($"{prompt} {defaultStr} ");
                string input = Console.ReadLine()?.Trim().ToLower();

                if (string.IsNullOrEmpty(input))
                    return defaultValue;

                if (input == "y" || input == "yes")
                    return true;

                if (input == "n" || input == "no")
                    return false;

                Console.WriteLine("请输入 Y 或 N!");
            }
        }
    }
}
