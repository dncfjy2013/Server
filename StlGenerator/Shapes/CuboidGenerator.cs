using OpenTK.Mathematics;
using StlGenerator.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StlGenerator.Shapes
{
    /// <summary>
    /// 长方体生成器
    /// </summary>
    public class CuboidGenerator : ShapeGenerator
    {
        private readonly float _length;
        private readonly float _width;
        private readonly float _height;

        public CuboidGenerator(float length, float width, float height)
        {
            _length = length;
            _width = width;
            _height = height;
        }

        public override string ShapeName => "Cuboid";

        protected override void GenerateShapeGeometry()
        {
            // 定义长方体的8个顶点
            Vector3 v0 = new Vector3(0, 0, 0);
            Vector3 v1 = new Vector3(_length, 0, 0);
            Vector3 v2 = new Vector3(_length, _width, 0);
            Vector3 v3 = new Vector3(0, _width, 0);
            Vector3 v4 = new Vector3(0, 0, _height);
            Vector3 v5 = new Vector3(_length, 0, _height);
            Vector3 v6 = new Vector3(_length, _width, _height);
            Vector3 v7 = new Vector3(0, _width, _height);

            // 底面
            AddTriangle(new Vector3(0, 0, -1), v0, v1, v2, GenerateColorBasedOnNormal(new Vector3(0, 0, -1)));
            AddTriangle(new Vector3(0, 0, -1), v0, v2, v3, GenerateColorBasedOnNormal(new Vector3(0, 0, -1)));

            // 顶面
            AddTriangle(new Vector3(0, 0, 1), v4, v6, v5, GenerateColorBasedOnNormal(new Vector3(0, 0, 1)));
            AddTriangle(new Vector3(0, 0, 1), v4, v7, v6, GenerateColorBasedOnNormal(new Vector3(0, 0, 1)));

            // 前面
            AddTriangle(new Vector3(-1, 0, 0), v0, v4, v1, GenerateColorBasedOnNormal(new Vector3(-1, 0, 0)));
            AddTriangle(new Vector3(-1, 0, 0), v1, v4, v5, GenerateColorBasedOnNormal(new Vector3(-1, 0, 0)));

            // 右面
            AddTriangle(new Vector3(0, -1, 0), v1, v5, v6, GenerateColorBasedOnNormal(new Vector3(0, -1, 0)));
            AddTriangle(new Vector3(0, -1, 0), v1, v6, v2, GenerateColorBasedOnNormal(new Vector3(0, -1, 0)));

            // 后面
            AddTriangle(new Vector3(1, 0, 0), v2, v6, v7, GenerateColorBasedOnNormal(new Vector3(1, 0, 0)));
            AddTriangle(new Vector3(1, 0, 0), v2, v7, v3, GenerateColorBasedOnNormal(new Vector3(1, 0, 0)));

            // 左面
            AddTriangle(new Vector3(0, 1, 0), v3, v7, v4, GenerateColorBasedOnNormal(new Vector3(0, 1, 0)));
            AddTriangle(new Vector3(0, 1, 0), v3, v4, v0, GenerateColorBasedOnNormal(new Vector3(0, 1, 0)));
        }
    }
}
