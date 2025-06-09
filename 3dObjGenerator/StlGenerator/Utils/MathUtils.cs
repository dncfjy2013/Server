using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StlGenerator.Utils
{
    /// <summary>
    /// 数学工具类，提供各种数学计算和转换功能
    /// </summary>
    public static class MathUtils
    {
        /// <summary>
        /// 计算两个向量之间的夹角(弧度)
        /// </summary>
        public static float AngleBetweenVectors(Vector3 v1, Vector3 v2)
        {
            float dot = Vector3.Dot(v1, v2);
            float magnitudeProduct = v1.Length * v2.Length;

            if (magnitudeProduct == 0)
                return 0;

            // 防止浮点数精度问题导致的反三角函数参数越界
            float cosTheta = MathHelper.Clamp(dot / magnitudeProduct, -1.0f, 1.0f);
            return (float)Math.Acos(cosTheta);
        }

        /// <summary>
        /// 将欧拉角(弧度)转换为四元数
        /// </summary>
        public static Quaternion EulerToQuaternion(float x, float y, float z)
        {
            float halfX = x * 0.5f;
            float halfY = y * 0.5f;
            float halfZ = z * 0.5f;

            float sinX = (float)Math.Sin(halfX);
            float cosX = (float)Math.Cos(halfX);
            float sinY = (float)Math.Sin(halfY);
            float cosY = (float)Math.Cos(halfY);
            float sinZ = (float)Math.Sin(halfZ);
            float cosZ = (float)Math.Cos(halfZ);

            Quaternion result = new Quaternion
            {
                W = cosX * cosY * cosZ + sinX * sinY * sinZ,
                X = sinX * cosY * cosZ - cosX * sinY * sinZ,
                Y = cosX * sinY * cosZ + sinX * cosY * sinZ,
                Z = cosX * cosY * sinZ - sinX * sinY * cosZ
            };

            return result.Normalized();
        }

        /// <summary>
        /// 将点从一个坐标系转换到另一个坐标系
        /// </summary>
        public static Vector3 TransformPoint(Vector3 point, Matrix4 transform)
        {
            Vector4 result = new Vector4(point, 1.0f) * transform;
            return new Vector3(result.X, result.Y, result.Z) / result.W;
        }

        /// <summary>
        /// 将向量从一个坐标系转换到另一个坐标系(只考虑旋转和缩放)
        /// </summary>
        public static Vector3 TransformVector(Vector3 vector, Matrix4 transform)
        {
            Vector4 result = new Vector4(vector, 0.0f) * transform;
            return new Vector3(result.X, result.Y, result.Z);
        }

        /// <summary>
        /// 生成随机颜色
        /// </summary>
        public static Color4 RandomColor(Random random = null)
        {
            random = random ?? new Random();

            return new Color4(
                (float)random.NextDouble(),
                (float)random.NextDouble(),
                (float)random.NextDouble(),
                1.0f
            );
        }

        /// <summary>
        /// 线性插值
        /// </summary>
        public static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        /// <summary>
        /// 向量线性插值
        /// </summary>
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t)
        {
            return new Vector3(
                Lerp(a.X, b.X, t),
                Lerp(a.Y, b.Y, t),
                Lerp(a.Z, b.Z, t)
            );
        }
    }
}
