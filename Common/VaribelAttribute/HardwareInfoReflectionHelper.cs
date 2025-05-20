using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Common.VaribelAttribute
{
    public static class HardwareInfoReflectionHelper
    {
        // 获取枚举值的硬件信息特性
        public static HardwareInfoAttribute GetHardwareInfo(this Enum value)
        {
            FieldInfo field = value.GetType().GetField(value.ToString());
            return field?.GetCustomAttribute<HardwareInfoAttribute>();
        }

        // 获取枚举值的所有描述信息
        public static string GetFullDescription(this Enum value)
        {
            var info = value.GetHardwareInfo();
            if (info == null) return value.ToString();

            string result = $"{info.FullName}: {info.Description}\n";
            foreach (var metric in info.PerformanceMetrics)
            {
                result += $"- {metric.Key}: {metric.Value}\n";
            }
            return result;
        }

        // 获取枚举类型的所有值及其描述
        public static Dictionary<TEnum, string> GetAllEnumDescriptions<TEnum>()
            where TEnum : struct, Enum
        {
            var result = new Dictionary<TEnum, string>();
            foreach (TEnum value in Enum.GetValues(typeof(TEnum)))
            {
                result[value] = value.GetFullDescription();
            }
            return result;
        }
    }
}
