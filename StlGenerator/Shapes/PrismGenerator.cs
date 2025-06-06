using OpenTK.Mathematics;
using StlGenerator.Core;
using System;
using System.Collections.Generic;

namespace StlGenerator.Shapes
{
    /// <summary>
    /// 多棱柱生成器（Y轴向上，右手坐标系）
    /// </summary>
    public class PrismGenerator : ShapeGenerator
    {
        private readonly int _sides;        // 底面边数
        private readonly float _radius;     // 底面半径
        private readonly float _height;     // Y轴方向高度

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
            GenerateBottomFace();   // 生成底面（Y=0平面）
            GenerateTopFace();      // 生成顶面（Y=height平面）
            GenerateSideFaces();    // 生成侧面
        }

        private void GenerateBottomFace()
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

        private void GenerateTopFace()
        {
            // 顶面位于Y=height平面，法向量朝Y轴正方向
            Vector3 normal = Vector3.UnitY;
            Color4 color = GenerateColorBasedOnNormal(normal);

            // 计算顶面多边形顶点（XZ平面上的正多边形，Y=height）
            Vector3[] vertices = new Vector3[_sides];
            for (int i = 0; i < _sides; i++)
            {
                float angle = i * 2 * MathHelper.Pi / _sides;
                vertices[i] = new Vector3(
                    _radius * (float)Math.Cos(angle),
                    _height,                           // Y坐标（顶面在Y=height）
                    _radius * (float)Math.Sin(angle)
                );
            }

            // 生成顶面扇形三角形（中心-顶点i-顶点i+1）
            for (int i = 0; i < _sides; i++)
            {
                int nextIndex = (i + 1) % _sides;
                AddTriangle(normal, new Vector3(0, _height, 0), vertices[i], vertices[nextIndex], color);
            }
        }

        private void GenerateSideFaces()
        {
            // 计算底面和顶面的多边形顶点
            Vector3[] bottomVertices = new Vector3[_sides];
            Vector3[] topVertices = new Vector3[_sides];

            for (int i = 0; i < _sides; i++)
            {
                float angle = i * 2 * MathHelper.Pi / _sides;
                bottomVertices[i] = new Vector3(
                    _radius * (float)Math.Cos(angle),
                    0,
                    _radius * (float)Math.Sin(angle)
                );

                topVertices[i] = new Vector3(
                    _radius * (float)Math.Cos(angle),
                    _height,
                    _radius * (float)Math.Sin(angle)
                );
            }

            // 生成侧面
            for (int i = 0; i < _sides; i++)
            {
                int nextIndex = (i + 1) % _sides;

                // 计算侧面法向量（径向向外，XZ平面方向）
                float midAngle = (i * 2 * MathHelper.Pi / _sides + (i + 1) * 2 * MathHelper.Pi / _sides) / 2;
                Vector3 normal = new Vector3(
                    (float)Math.Cos(midAngle),  // X分量
                    0,                          // Y分量（侧面法向量在XZ平面）
                    (float)Math.Sin(midAngle)   // Z分量
                ).Normalized();

                Color4 color = GenerateColorBasedOnNormal(normal);

                // 生成两个三角形组成侧面矩形
                AddTriangle(normal, bottomVertices[i], topVertices[i], topVertices[nextIndex], color);
                AddTriangle(normal, bottomVertices[i], topVertices[nextIndex], bottomVertices[nextIndex], color);
            }
        }
    }
}