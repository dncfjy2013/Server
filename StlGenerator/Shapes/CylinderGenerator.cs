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
    /// 圆柱体生成器
    /// </summary>
    public class CylinderGenerator : ShapeGenerator
    {
        private readonly float _radius;
        private readonly float _height;
        private readonly int _sides;

        public CylinderGenerator(float radius, float height, int sides = 32)
        {
            _radius = radius;
            _height = height;
            _sides = sides;
        }

        public override string ShapeName => "Cylinder";

        protected override void GenerateShapeGeometry()
        {
            // 生成顶面和底面
            GenerateTopFace();
            GenerateBottomFace();
            GenerateSideFaces();
        }

        private void GenerateTopFace()
        {
            Vector3 center = new Vector3(0, 0, _height);
            Vector3 normal = new Vector3(0, 0, 1);
            Color4 color = GenerateColorBasedOnNormal(normal);

            for (int i = 0; i < _sides; i++)
            {
                float angle1 = i * 2 * MathHelper.Pi / _sides;
                float angle2 = (i + 1) * 2 * MathHelper.Pi / _sides;

                Vector3 v1 = new Vector3(
                    _radius * (float)Math.Cos(angle1),
                    _radius * (float)Math.Sin(angle1),
                    _height
                );

                Vector3 v2 = new Vector3(
                    _radius * (float)Math.Cos(angle2),
                    _radius * (float)Math.Sin(angle2),
                    _height
                );

                AddTriangle(normal, center, v1, v2, color);
            }
        }

        private void GenerateBottomFace()
        {
            Vector3 center = new Vector3(0, 0, 0);
            Vector3 normal = new Vector3(0, 0, -1);
            Color4 color = GenerateColorBasedOnNormal(normal);

            for (int i = 0; i < _sides; i++)
            {
                float angle1 = i * 2 * MathHelper.Pi / _sides;
                float angle2 = (i + 1) * 2 * MathHelper.Pi / _sides;

                Vector3 v1 = new Vector3(
                    _radius * (float)Math.Cos(angle1),
                    _radius * (float)Math.Sin(angle1),
                    0
                );

                Vector3 v2 = new Vector3(
                    _radius * (float)Math.Cos(angle2),
                    _radius * (float)Math.Sin(angle2),
                    0
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

                // 顶部两个点
                Vector3 v1 = new Vector3(
                    _radius * (float)Math.Cos(angle1),
                    _radius * (float)Math.Sin(angle1),
                    _height
                );

                Vector3 v2 = new Vector3(
                    _radius * (float)Math.Cos(angle2),
                    _radius * (float)Math.Sin(angle2),
                    _height
                );

                // 底部两个点
                Vector3 v3 = new Vector3(
                    _radius * (float)Math.Cos(angle2),
                    _radius * (float)Math.Sin(angle2),
                    0
                );

                Vector3 v4 = new Vector3(
                    _radius * (float)Math.Cos(angle1),
                    _radius * (float)Math.Sin(angle1),
                    0
                );

                // 计算侧面法向量 (径向向外)
                Vector3 normal = new Vector3(
                    (float)Math.Cos((angle1 + angle2) / 2),
                    (float)Math.Sin((angle1 + angle2) / 2),
                    0
                );

                Color4 color = GenerateColorBasedOnNormal(normal);

                // 生成两个三角形组成一个矩形面
                AddTriangle(normal, v1, v2, v3, color);
                AddTriangle(normal, v1, v3, v4, color);
            }
        }
    }
}
