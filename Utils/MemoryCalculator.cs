using Server.Extend;
using Server.Logger;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Server.Utils
{
    /// <summary>
    /// 内存计算工具类（计算对象实例的内存占用大小）
    /// </summary>
    public static class MemoryCalculator
    {
        /// <summary>
        /// 计算指定对象的内存占用大小（基于反射分析对象字段）
        /// </summary>
        /// <typeparam name="T">对象类型（需为引用类型）</typeparam>
        /// <param name="obj">待计算的对象实例</param>
        /// <returns>对象的内存占用大小（字节）</returns>
        public static long CalculateObjectSize<T>(T obj) where T : class
        {
            if (obj == null)
            {
                // 记录 Error 日志：传入空对象
                LoggerInstance.Instance.LogError("Cannot calculate size of null object");
                return 0;
            }

            long size = 0;
            Type type = typeof(T);

            // 记录 Trace 日志：开始计算对象内存
            LoggerInstance.Instance.LogTrace($"Calculating memory size for object of type {type.FullName}");

            try
            {
                // 对象头开销（同步块索引 + 类型指针，固定为 16 字节）
                size += 16;
                LoggerInstance.Instance.LogDebug($"Added object header size: 16 bytes");

                // 遍历对象所有实例字段（包括公有和私有字段）
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    try
                    {
                        object value = field.GetValue(obj);
                        Type fieldType = field.FieldType;
                        string fieldName = field.Name;

                        // 记录 Debug 日志：处理当前字段
                        LoggerInstance.Instance.LogDebug($"Processing field: {fieldName} ({fieldType.Name})");

                        if (fieldType.IsPrimitive)
                        {
                            // 基础类型（如 int、double 等）直接计算 Marshal 大小
                            long primitiveSize = Marshal.SizeOf(fieldType);
                            size += primitiveSize;
                            LoggerInstance.Instance.LogTrace($"Primitive type {fieldType.Name}, size: {primitiveSize} bytes");
                        }
                        else if (fieldType.IsEnum)
                        {
                            // 枚举类型按基础类型大小计算（如 int、long 等）
                            Type enumUnderlyingType = Enum.GetUnderlyingType(fieldType);
                            long enumSize = Marshal.SizeOf(enumUnderlyingType);
                            size += enumSize;
                            LoggerInstance.Instance.LogTrace($"Enum type {fieldType.Name} (underlying {enumUnderlyingType.Name}), size: {enumSize} bytes");
                        }
                        else if (fieldType == typeof(string))
                        {
                            // 字符串特殊处理：包含对象头、字符数组和字符串内容
                            if (value != null)
                            {
                                string str = (string)value;
                                // 字符串对象头：24 字节（16字节对象头 + 8字节数组引用）
                                size += 24;
                                // 字符数组：每个字符占 2 字节（Unicode），加上数组头 16 字节
                                size += (long)str.Length * 2 + 16;
                                LoggerInstance.Instance.LogDebug($"String value '{str.Substring(0, Math.Min(str.Length, 20))}...', size: {24 + str.Length * 2 + 16} bytes");
                            }
                            else
                            {
                                // 空引用：指针大小（32位/64位系统）
                                size += IntPtr.Size;
                                LoggerInstance.Instance.LogTrace($"Null string reference, size: {IntPtr.Size} bytes");
                            }
                        }
                        else if (fieldType.IsArray)
                        {
                            // 数组处理：包含数组头和元素数据
                            Array array = value as Array;
                            if (array != null)
                            {
                                Type elementType = fieldType.GetElementType();
                                long elementSize = Marshal.SizeOf(elementType);
                                // 数组头：16 字节
                                size += 16;
                                // 元素总大小
                                size += array.Length * elementSize;
                                LoggerInstance.Instance.LogDebug($"Array of {elementType.Name} ({array.Length} elements), size: 16 + {array.Length * elementSize} = {16 + array.Length * elementSize} bytes");
                            }
                            else
                            {
                                // 空数组引用
                                size += IntPtr.Size;
                                LoggerInstance.Instance.LogTrace($"Null array reference, size: {IntPtr.Size} bytes");
                            }
                        }
                        else
                        {
                            // 其他引用类型：仅计算指针大小
                            size += IntPtr.Size;
                            LoggerInstance.Instance.LogTrace($"Reference type {fieldType.Name}, size: {IntPtr.Size} bytes");
                        }
                    }
                    catch (Exception ex)
                    {
                        // 记录 Error 日志：字段读取异常（如非公共字段无访问权限）
                        LoggerInstance.Instance.LogError($"Error getting field {field.Name} value: {ex.Message} {ex}");
                    }
                }

                // 记录 Info 日志：返回计算结果
                LoggerInstance.Instance.LogDebug($"Calculated memory size for {type.FullName}: {size} bytes");
                return size;
            }
            catch (Exception ex)
            {
                // 记录 Critical 日志：内存计算过程中出现致命异常
                LoggerInstance.Instance.LogCritical($"Fatal error in memory calculation for {type.FullName}: {ex.Message} {ex}");
                throw;
            }
        }
    }
}
