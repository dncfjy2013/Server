using System.Reflection;
using System.Runtime.InteropServices;

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
                return 0;
            }

            long size = 0;
            Type type = typeof(T);

            try
            {
                // 对象头开销（同步块索引 + 类型指针，固定为 16 字节）
                size += 16;

                // 遍历对象所有实例字段（包括公有和私有字段）
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    try
                    {
                        object value = field.GetValue(obj);
                        Type fieldType = field.FieldType;
                        string fieldName = field.Name;

                        if (fieldType.IsPrimitive)
                        {
                            // 基础类型（如 int、double 等）直接计算 Marshal 大小
                            long primitiveSize = Marshal.SizeOf(fieldType);
                            size += primitiveSize;
                        }
                        else if (fieldType.IsEnum)
                        {
                            // 枚举类型按基础类型大小计算（如 int、long 等）
                            Type enumUnderlyingType = Enum.GetUnderlyingType(fieldType);
                            long enumSize = Marshal.SizeOf(enumUnderlyingType);
                            size += enumSize;
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
                            }
                            else
                            {
                                // 空引用：指针大小（32位/64位系统）
                                size += IntPtr.Size;
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
                            }
                            else
                            {
                                // 空数组引用
                                size += IntPtr.Size;
                            }
                        }
                        else
                        {
                            // 其他引用类型：仅计算指针大小
                            size += IntPtr.Size;
                        }
                    }
                    catch (Exception ex)
                    {

                    }
                }

                return size;
            }
            catch (Exception ex)
            {
                throw;
            }
        }
    }
}
