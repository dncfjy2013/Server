using OpenTK.Mathematics;
using StlGenerator.Core;
using System;
using System.Collections.Generic;

namespace StlGenerator.Shapes
{
    /// <summary>
    /// 椭圆生成器（Y轴向上，右手坐标系）
    /// </summary>
    public class EllipseGenerator : ShapeGenerator
    {
        private readonly float _radiusX;      // X轴方向半径
        private readonly float _radiusZ;      // Z轴方向半径
        private readonly float _thickness;    // Y轴方向厚度
        private readonly int _segments;       // 圆周分段数

        public EllipseGenerator(float radiusX, float radiusZ, float thickness = 0.1f, int segments = 32)
        {
            if (segments < 3)
                throw new ArgumentException("分段数至少为3", nameof(segments));

            _radiusX = radiusX;
            _radiusZ = radiusZ;
            _thickness = thickness;
            _segments = segments;
        }

        public override string ShapeName => "Ellipse";

        protected override void GenerateShapeGeometry()
        {
            GenerateTopFace();       // 顶面（Y轴正方向）
            GenerateBottomFace();    // 底面（Y轴负方向）
            GenerateSideFaces();     // 侧面
        }

        private void GenerateTopFace()
        {
            // 顶面位于Y轴正方向（Y=thickness/2），法向量朝Y轴正方向
            Vector3 normal = Vector3.UnitY;
            float yOffset = _thickness / 2;
            Color4 color = GenerateColorBasedOnNormal(normal);

            for (int i = 0; i < _segments; i++)
            {
                float angle1 = i * 2 * MathHelper.Pi / _segments;
                float angle2 = (i + 1) * 2 * MathHelper.Pi / _segments;

                // 计算顶面顶点（XZ平面椭圆，Y=yOffset）
                Vector3 v1 = new Vector3(
                    _radiusX * (float)Math.Cos(angle1),  // X坐标
                    yOffset,                            // Y坐标（顶面）
                    _radiusZ * (float)Math.Sin(angle1)   // Z坐标
                );

                Vector3 v2 = new Vector3(
                    _radiusX * (float)Math.Cos(angle2),
                    yOffset,
                    _radiusZ * (float)Math.Sin(angle2)
                );

                // 中心位于原点，偏移到顶面Y坐标
                AddTriangle(normal, new Vector3(0, yOffset, 0), v1, v2, color);
            }
        }

        private void GenerateBottomFace()
        {
            // 底面位于Y轴负方向（Y=-thickness/2），法向量朝Y轴负方向
            Vector3 normal = -Vector3.UnitY;
            float yOffset = -_thickness / 2;
            Color4 color = GenerateColorBasedOnNormal(normal);

            for (int i = 0; i < _segments; i++)
            {
                float angle1 = i * 2 * MathHelper.Pi / _segments;
                float angle2 = (i + 1) * 2 * MathHelper.Pi / _segments;

                // 计算底面顶点（XZ平面椭圆，Y=yOffset）
                Vector3 v1 = new Vector3(
                    _radiusX * (float)Math.Cos(angle1),
                    yOffset,                            // Y坐标（底面）
                    _radiusZ * (float)Math.Sin(angle1)
                );

                Vector3 v2 = new Vector3(
                    _radiusX * (float)Math.Cos(angle2),
                    yOffset,
                    _radiusZ * (float)Math.Sin(angle2)
                );

                AddTriangle(normal, new Vector3(0, yOffset, 0), v2, v1, color);
            }
        }

        private void GenerateSideFaces()
        {
            for (int i = 0; i < _segments; i++)
            {
                float angle1 = i * 2 * MathHelper.Pi / _segments;
                float angle2 = (i + 1) * 2 * MathHelper.Pi / _segments;
                float midAngle = (angle1 + angle2) / 2;

                // 计算侧面法向量（径向向外，XZ平面方向）
                Vector3 normal = new Vector3(
                    (float)Math.Cos(midAngle),  // X分量
                    0,                          // Y分量（侧面法向量在XZ平面）
                    (float)Math.Sin(midAngle)   // Z分量
                ).Normalized();

                Color4 color = GenerateColorBasedOnNormal(normal);

                // 顶面顶点
                float topY = _thickness / 2;
                Vector3 topV1 = new Vector3(
                    _radiusX * (float)Math.Cos(angle1),
                    topY,
                    _radiusZ * (float)Math.Sin(angle1)
                );

                Vector3 topV2 = new Vector3(
                    _radiusX * (float)Math.Cos(angle2),
                    topY,
                    _radiusZ * (float)Math.Sin(angle2)
                );

                // 底面顶点
                float bottomY = -_thickness / 2;
                Vector3 bottomV1 = new Vector3(
                    _radiusX * (float)Math.Cos(angle1),
                    bottomY,
                    _radiusZ * (float)Math.Sin(angle1)
                );

                Vector3 bottomV2 = new Vector3(
                    _radiusX * (float)Math.Cos(angle2),
                    bottomY,
                    _radiusZ * (float)Math.Sin(angle2)
                );

                // 生成两个三角形组成侧面矩形
                AddTriangle(normal, topV1, topV2, bottomV2, color);
                AddTriangle(normal, topV1, bottomV2, bottomV1, color);
            }
        }
    }
}