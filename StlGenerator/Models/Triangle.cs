using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StlGenerator.Models
{
    /// <summary>
    /// 三角形面数据结构，表示3D模型的基本几何单元
    /// </summary>
    public struct Triangle
    {
        /// <summary>三角形的法向量，用于光照计算</summary>
        public Vector3 Normal;

        /// <summary>三角形的第一个顶点坐标</summary>
        public Vector3 Vertex1;

        /// <summary>三角形的第二个顶点坐标</summary>
        public Vector3 Vertex2;

        /// <summary>三角形的第三个顶点坐标</summary>
        public Vector3 Vertex3;

        /// <summary>三角形的颜色属性（RGBA）</summary>
        public Color4 Color;

        /// <summary>
        /// 构造三角形实例
        /// </summary>
        /// <param name="normal">三角形法向量</param>
        /// <param name="v1">第一个顶点坐标</param>
        /// <param name="v2">第二个顶点坐标</param>
        /// <param name="v3">第三个顶点坐标</param>
        /// <param name="color">三角形颜色</param>
        public Triangle(Vector3 normal, Vector3 v1, Vector3 v2, Vector3 v3, Color4 color)
        {
            Normal = normal;
            Vertex1 = v1;
            Vertex2 = v2;
            Vertex3 = v3;
            Color = color;
        }
    }
}