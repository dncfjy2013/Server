using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace NeuralNet.Common
{
    /// <summary>
    /// JSON数组与float数组转换工具类
    /// </summary>
    internal static class JsonArrayHelper
    {
        /// <summary>
        /// 将float数组转换为JsonArray
        /// </summary>
        public static JsonArray FromFloatArray(float[] data)
        {
            if (data == null)
                return null;

            JsonArray array = new JsonArray();
            foreach (float val in data)
                array.Add(val);
            return array;
        }

        /// <summary>
        /// 将JsonArray转换为float数组
        /// </summary>
        public static float[] ToFloatArray(JsonArray array)
        {
            if (array == null)
                return null;

            float[] data = new float[array.Count];
            for (int i = 0; i < array.Count; i++)
            {
                data[i] = array[i]?.GetValue<float>() ?? 0;
            }
            return data;
        }
    }
}
