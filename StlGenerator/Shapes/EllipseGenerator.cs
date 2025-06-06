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
    /// 椭圆生成器（生成薄的椭圆盘）
    /// </summary>
    public class EllipseGenerator : ShapeGenerator
    {
        private readonly float _radiusX;
        private readonly float _radiusY;
        private readonly float _thickness;
        private readonly int _segments;

        public EllipseGenerator(float radiusX, float radiusY, float thickness = 0.1f, int segments = 32)
        {
            _radiusX = radiusX;
            _radiusY = radiusY;
            _thickness = thickness;
            _segments = segments;
        }

        public override string ShapeName => "Ellipse";

        protected override void GenerateShapeGeometry()
        {
            // 生成顶面和底面
            GenerateFace(Vector3.UnitZ, 0);  // 顶面
            GenerateFace(-Vector3.UnitZ, _thickness);  // 底面

            // 生成侧面
            GenerateSide();
        }

        private void GenerateFace(Vector3 normal, float zOffset)
        {
            Color4 color = GenerateColorBasedOnNormal(normal);

            for (int i = 0; i < _segments; i++)
            {
                float angle1 = i * 2 * MathHelper.Pi / _segments;
                float angle2 = (i + 1) * 2 * MathHelper.Pi / _segments;

                Vector3 v1 = new Vector3(
                    _radiusX * (float)Math.Cos(angle1),
                    _radiusY * (float)Math.Sin(angle1),
                    zOffset
                );

                Vector3 v2 = new Vector3(
                    _radiusX * (float)Math.Cos(angle2),
                    _radiusY * (float)Math.Sin(angle2),
                    zOffset
                );

                AddTriangle(normal, Vector3.Zero + new Vector3(0, 0, zOffset), v1, v2, color);
            }
        }

        private void GenerateSide()
        {
            for (int i = 0; i < _segments; i++)
            {
                float angle1 = i * 2 * MathHelper.Pi / _segments;
                float angle2 = (i + 1) * 2 * MathHelper.Pi / _segments;

                // 计算侧面法向量（径向向外）
                float midAngle = (angle1 + angle2) / 2;
                Vector3 normal = new Vector3(
                    (float)Math.Cos(midAngle),
                    (float)Math.Sin(midAngle),
                    0
                ).Normalized();

                Color4 color = GenerateColorBasedOnNormal(normal);

                // 顶部两个点
                Vector3 v1Top = new Vector3(
                    _radiusX * (float)Math.Cos(angle1),
                    _radiusY * (float)Math.Sin(angle1),
                    0
                );

                Vector3 v2Top = new Vector3(
                    _radiusX * (float)Math.Cos(angle2),
                    _radiusY * (float)Math.Sin(angle2),
                    0
                );

                // 底部两个点
                Vector3 v1Bottom = v1Top + new Vector3(0, 0, _thickness);
                Vector3 v2Bottom = v2Top + new Vector3(0, 0, _thickness);

                // 生成两个三角形组成一个矩形面
                AddTriangle(normal, v1Top, v2Top, v2Bottom, color);
                AddTriangle(normal, v1Top, v2Bottom, v1Bottom, color);
            }
        }
    }
}
