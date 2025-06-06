using OpenTK.Mathematics;
using StlGenerator.Core;
using System;
using System.Collections.Generic;

namespace StlGenerator.Shapes
{
    /// <summary>
    /// 多棱锥生成器（Y轴向上，右手坐标系）
    /// </summary>
    public class PolyPyramidGenerator : ShapeGenerator
    {
        private readonly int _sides;        // 底面边数
        private readonly float _radius;     // 底面半径
        private readonly float _height;     // Y轴方向高度

        public PolyPyramidGenerator(int sides, float radius, float height)
        {
            if (sides < 3)
                throw new ArgumentException("多棱锥的边数必须至少为3", nameof(sides));

            _sides = sides;
            _radius = radius;
            _height = height;
        }

        public override string ShapeName => $"{_sides}边形棱锥";

        protected override void GenerateShapeGeometry()
        {
            GenerateBase();     // 生成底面（Y=0平面）
            GenerateSides();    // 生成侧面
        }

        private void GenerateBase()
        {
            // 底面位于Y=0平面，法向量朝Y轴负方向
            Vector3 normal = -Vector3.UnitY;
            Color4 color = GenerateColorBasedOnNormal(normal);

            // 计算底面多边形顶点（XZ平面上的正多边形）
            Vector3[] vertices = new Vector3[_sides];
            for (int i = 0; i < _sides; i++)
            {
                float angle = i * 2 * MathHelper.Pi / _sides;
                vertices[i] = new Vector3(
                    _radius * (float)Math.Cos(angle),  // X坐标
                    0,                                 // Y坐标（底面在Y=0）
                    _radius * (float)Math.Sin(angle)   // Z坐标
                );
            }

            // 生成底面扇形三角形（中心-顶点i-顶点i+1）
            for (int i = 0; i < _sides; i++)
            {
                int nextIndex = (i + 1) % _sides;
                AddTriangle(normal, Vector3.Zero, vertices[i], vertices[nextIndex], color);
            }
        }

        private void GenerateSides()
        {
            // 顶点位于Y轴正方向（Y=height）
            Vector3 apex = new Vector3(0, _height, 0);

            // 计算底面多边形顶点
            Vector3[] baseVertices = new Vector3[_sides];
            for (int i = 0; i < _sides; i++)
            {
                float angle = i * 2 * MathHelper.Pi / _sides;
                baseVertices[i] = new Vector3(
                    _radius * (float)Math.Cos(angle),
                    0,
                    _radius * (float)Math.Sin(angle)
                );
            }

            // 生成侧面三角形
            for (int i = 0; i < _sides; i++)
            {
                int nextIndex = (i + 1) % _sides;

                // 计算侧面法向量（右手定则）
                Vector3 v1 = baseVertices[i];
                Vector3 v2 = baseVertices[nextIndex];
                Vector3 normal = Vector3.Normalize(Vector3.Cross(v2 - apex, v1 - apex));
                // 注意向量顺序：v2-apex 到 v1-apex，确保法向量朝外

                Color4 color = GenerateColorBasedOnNormal(normal);

                // 添加侧面三角形（顶点-顶点i-顶点i+1）
                AddTriangle(normal, apex, v1, v2, color);
            }
        }
    }
}