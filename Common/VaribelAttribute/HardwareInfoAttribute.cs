using System;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;

namespace Common.VaribelAttribute
{
    // 硬件信息特性类，用于存储枚举值的详细信息
    [AttributeUsage(AttributeTargets.Field)]
    public class HardwareInfoAttribute : Attribute
    {
        public string FullName { get; set; }
        public string Description { get; set; }
        public Dictionary<string, string> PerformanceMetrics { get; set; }

        public HardwareInfoAttribute(string fullName, string description, params string[] metrics)
        {
            FullName = fullName;
            Description = description;
            PerformanceMetrics = new Dictionary<string, string>();

            // 解析性能指标（格式："指标名称:指标值"）
            for (int i = 0; i < metrics.Length; i++)
            {
                string[] parts = metrics[i].Split(':');
                if (parts.Length == 2)
                {
                    PerformanceMetrics[parts[0]] = parts[1];
                }
            }
        }
    }
}
