using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StlGenerator.Models
{
    /// <summary>
    /// 三角形面数据结构
    /// </summary>
    public struct Triangle
    {
        public Vector3 Normal;
        public Vector3 Vertex1;
        public Vector3 Vertex2;
        public Vector3 Vertex3;
        public Color4 Color;

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
