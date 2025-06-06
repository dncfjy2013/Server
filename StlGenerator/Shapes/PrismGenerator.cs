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
    /// 多棱柱生成器
    /// </summary>
    public class PrismGenerator : ShapeGenerator
    {
        private readonly int _sides;
        private readonly float _radius;
        private readonly float _height;

        public PrismGenerator(int sides, float radius, float height)
        {
            if (sides < 3)
                throw new ArgumentException("多棱柱的边数必须至少为3", nameof(sides));

            _sides = sides;
            _radius = radius;
            _height = height;
        }

        public override string ShapeName => $"{_sides}边形棱柱";

        protected override void GenerateShapeGeometry()
        {
            // 生成底面和顶面
            GenerateFace(-Vector3.UnitZ, 0);  // 底面
            GenerateFace(Vector3.UnitZ, _height);  // 顶面

            // 生成侧面
            GenerateSides();
        }

        private void GenerateFace(Vector3 normal, float zOffset)
        {
            Color4 color = GenerateColorBasedOnNormal(normal);

            // 计算多边形顶点
            Vector3[] vertices = new Vector3[_sides];
            for (int i = 0; i < _sides; i++)
            {
                float angle = i * 2 * MathHelper.Pi / _sides;
                vertices[i] = new Vector3(
                    _radius * (float)Math.Cos(angle),
                    _radius * (float)Math.Sin(angle),
                    zOffset
                );
            }

            // 生成扇形三角形
            for (int i = 0; i < _sides; i++)
            {
                int nextIndex = (i + 1) % _sides;
                AddTriangle(normal, Vector3.Zero + new Vector3(0, 0, zOffset), vertices[i], vertices[nextIndex], color);
            }
        }

        private void GenerateSides()
        {
            // 计算多边形顶点
            Vector3[] bottomVertices = new Vector3[_sides];
            Vector3[] topVertices = new Vector3[_sides];

            for (int i = 0; i < _sides; i++)
            {
                float angle = i * 2 * MathHelper.Pi / _sides;
                bottomVertices[i] = new Vector3(
                    _radius * (float)Math.Cos(angle),
                    _radius * (float)Math.Sin(angle),
                    0
                );

                topVertices[i] = bottomVertices[i] + new Vector3(0, 0, _height);
            }

            // 生成侧面
            for (int i = 0; i < _sides; i++)
            {
                int nextIndex = (i + 1) % _sides;

                // 计算侧面法向量
                Vector3 v1 = bottomVertices[i];
                Vector3 v2 = bottomVertices[nextIndex];
                Vector3 normal = Vector3.Normalize(Vector3.Cross(v2 - v1, Vector3.UnitZ));

                Color4 color = GenerateColorBasedOnNormal(normal);

                // 生成两个三角形组成侧面矩形
                AddTriangle(normal, bottomVertices[i], bottomVertices[nextIndex], topVertices[nextIndex], color);
                AddTriangle(normal, bottomVertices[i], topVertices[nextIndex], topVertices[i], color);
            }
        }
    }
}
