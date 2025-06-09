using OpenTK.Mathematics;
using StlGenerator.Core;
using System;
using System.Collections.Generic;

namespace StlGenerator.Shapes
{
    /// <summary>
    /// 圆柱体生成器（Y轴向上，右手坐标系）
    /// </summary>
    public class CylinderGenerator : ShapeGenerator
    {
        private readonly float _radius;       // 底面半径
        private readonly float _height;       // Y轴方向高度
        private readonly int _sides;          // 侧面分段数

        public CylinderGenerator(float radius, float height, int sides = 32)
        {
            if (sides < 3)
                throw new ArgumentException("侧面分段数至少为3", nameof(sides));

            _radius = radius;
            _height = height;
            _sides = sides;
        }

        public override string ShapeName => "Cylinder";

        protected override void GenerateShapeGeometry()
        {
            GenerateTopFace();
            GenerateBottomFace();
            GenerateSideFaces();
        }

        private void GenerateTopFace()
        {
            // 顶面中心在Y轴正方向（Y=height）
            Vector3 center = new Vector3(0, _height, 0);
            Vector3 normal = Vector3.UnitY;            // 顶面法向量朝Y轴正方向
            Color4 color = GenerateColorBasedOnNormal(normal);

            for (int i = 0; i < _sides; i++)
            {
                float angle1 = i * 2 * MathHelper.Pi / _sides;
                float angle2 = (i + 1) * 2 * MathHelper.Pi / _sides;

                // 顶面顶点（XZ平面上的圆，Y=height）
                Vector3 v1 = new Vector3(
                    _radius * (float)Math.Cos(angle1),  // X坐标
                    _height,                            // Y坐标（顶面在Y=height）
                    _radius * (float)Math.Sin(angle1)   // Z坐标
                );

                Vector3 v2 = new Vector3(
                    _radius * (float)Math.Cos(angle2),
                    _height,
                    _radius * (float)Math.Sin(angle2)
                );

                AddTriangle(normal, center, v1, v2, color);
            }
        }

        private void GenerateBottomFace()
        {
            // 底面中心在原点（Y=0）
            Vector3 center = new Vector3(0, 0, 0);
            Vector3 normal = -Vector3.UnitY;           // 底面法向量朝Y轴负方向
            Color4 color = GenerateColorBasedOnNormal(normal);

            for (int i = 0; i < _sides; i++)
            {
                float angle1 = i * 2 * MathHelper.Pi / _sides;
                float angle2 = (i + 1) * 2 * MathHelper.Pi / _sides;

                // 底面顶点（XZ平面上的圆，Y=0）
                Vector3 v1 = new Vector3(
                    _radius * (float)Math.Cos(angle1),
                    0,                                 // Y坐标（底面在Y=0）
                    _radius * (float)Math.Sin(angle1)
                );

                Vector3 v2 = new Vector3(
                    _radius * (float)Math.Cos(angle2),
                    0,
                    _radius * (float)Math.Sin(angle2)
                );

                AddTriangle(normal, center, v2, v1, color);
            }
        }

        private void GenerateSideFaces()
        {
            for (int i = 0; i < _sides; i++)
            {
                float angle1 = i * 2 * MathHelper.Pi / _sides;
                float angle2 = (i + 1) * 2 * MathHelper.Pi / _sides;

                // 顶面顶点
                Vector3 topV1 = new Vector3(
                    _radius * (float)Math.Cos(angle1),
                    _height,
                    _radius * (float)Math.Sin(angle1)
                );

                Vector3 topV2 = new Vector3(
                    _radius * (float)Math.Cos(angle2),
                    _height,
                    _radius * (float)Math.Sin(angle2)
                );

                // 底面顶点
                Vector3 bottomV1 = new Vector3(
                    _radius * (float)Math.Cos(angle1),
                    0,
                    _radius * (float)Math.Sin(angle1)
                );

                Vector3 bottomV2 = new Vector3(
                    _radius * (float)Math.Cos(angle2),
                    0,
                    _radius * (float)Math.Sin(angle2)
                );

                // 计算侧面法向量（径向向外，XZ平面方向）
                float midAngle = (angle1 + angle2) / 2;
                Vector3 normal = new Vector3(
                    (float)Math.Cos(midAngle),  // X分量
                    0,                          // Y分量（侧面法向量在XZ平面）
                    (float)Math.Sin(midAngle)   // Z分量
                );

                Color4 color = GenerateColorBasedOnNormal(normal);

                // 生成两个三角形组成侧面矩形
                AddTriangle(normal, topV1, topV2, bottomV2, color);
                AddTriangle(normal, topV1, bottomV2, bottomV1, color);
            }
        }
    }
}