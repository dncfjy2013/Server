using OpenTK.Mathematics;
using StlGenerator.Core;
using System;
using System.Collections.Generic;

namespace StlGenerator.Shapes
{
    /// <summary>
    /// 长方体生成器（Y轴向上）
    /// </summary>
    public class CuboidGenerator : ShapeGenerator
    {
        private readonly float _length;   // X轴方向长度
        private readonly float _width;    // Z轴方向宽度
        private readonly float _height;   // Y轴方向高度

        public CuboidGenerator(float length, float width, float height)
        {
            _length = length;
            _width = width;
            _height = height;
        }

        public override string ShapeName => "Cuboid";

        protected override void GenerateShapeGeometry()
        {
            // 定义Y轴向上的长方体8个顶点（中心在原点）
            float halfLength = _length / 2;
            float halfWidth = _width / 2;
            float halfHeight = _height / 2;

            Vector3 v0 = new Vector3(-halfLength, -halfHeight, -halfWidth);  // 左前下
            Vector3 v1 = new Vector3(halfLength, -halfHeight, -halfWidth);   // 右前下
            Vector3 v2 = new Vector3(halfLength, -halfHeight, halfWidth);    // 右后下
            Vector3 v3 = new Vector3(-halfLength, -halfHeight, halfWidth);   // 左后下
            Vector3 v4 = new Vector3(-halfLength, halfHeight, -halfWidth);   // 左前上
            Vector3 v5 = new Vector3(halfLength, halfHeight, -halfWidth);    // 右前上
            Vector3 v6 = new Vector3(halfLength, halfHeight, halfWidth);     // 右后上
            Vector3 v7 = new Vector3(-halfLength, halfHeight, halfWidth);    // 左后上

            // 底面（Y轴负方向）
            AddTriangle(new Vector3(0, -1, 0), v0, v1, v2, GenerateColorBasedOnNormal(new Vector3(0, -1, 0)));
            AddTriangle(new Vector3(0, -1, 0), v0, v2, v3, GenerateColorBasedOnNormal(new Vector3(0, -1, 0)));

            // 顶面（Y轴正方向）
            AddTriangle(new Vector3(0, 1, 0), v4, v6, v5, GenerateColorBasedOnNormal(new Vector3(0, 1, 0)));
            AddTriangle(new Vector3(0, 1, 0), v4, v7, v6, GenerateColorBasedOnNormal(new Vector3(0, 1, 0)));

            // 前面（Z轴负方向）
            AddTriangle(new Vector3(0, 0, -1), v0, v4, v1, GenerateColorBasedOnNormal(new Vector3(0, 0, -1)));
            AddTriangle(new Vector3(0, 0, -1), v1, v4, v5, GenerateColorBasedOnNormal(new Vector3(0, 0, -1)));

            // 右面（X轴正方向）
            AddTriangle(new Vector3(1, 0, 0), v1, v5, v6, GenerateColorBasedOnNormal(new Vector3(1, 0, 0)));
            AddTriangle(new Vector3(1, 0, 0), v1, v6, v2, GenerateColorBasedOnNormal(new Vector3(1, 0, 0)));

            // 后面（Z轴正方向）
            AddTriangle(new Vector3(0, 0, 1), v2, v6, v7, GenerateColorBasedOnNormal(new Vector3(0, 0, 1)));
            AddTriangle(new Vector3(0, 0, 1), v2, v7, v3, GenerateColorBasedOnNormal(new Vector3(0, 0, 1)));

            // 左面（X轴负方向）
            AddTriangle(new Vector3(-1, 0, 0), v3, v7, v4, GenerateColorBasedOnNormal(new Vector3(-1, 0, 0)));
            AddTriangle(new Vector3(-1, 0, 0), v3, v4, v0, GenerateColorBasedOnNormal(new Vector3(-1, 0, 0)));
        }
    }
}