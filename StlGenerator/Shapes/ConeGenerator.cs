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
    /// 圆锥体生成器
    /// </summary>
    public class ConeGenerator : ShapeGenerator
    {
        private readonly float _radius;
        private readonly float _height;
        private readonly int _sides;

        public ConeGenerator(float radius, float height, int sides = 32)
        {
            _radius = radius;
            _height = height;
            _sides = sides;
        }

        public override string ShapeName => "Cone";

        protected override void GenerateShapeGeometry()
        {
            // 生成底面
            Vector3 apex = new Vector3(0, 0, _height);
            Vector3 center = new Vector3(0, 0, 0);
            Vector3 normal = new Vector3(0, 0, -1);
            Color4 baseColor = GenerateColorBasedOnNormal(normal);

            // 生成底面
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

                AddTriangle(normal, center, v2, v1, baseColor);
            }

            // 生成侧面
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

                // 计算侧面法向量
                Vector3 sideNormal = Vector3.Normalize(
                    Vector3.Cross(v1 - apex, v2 - apex)
                );

                Color4 sideColor = GenerateColorBasedOnNormal(sideNormal);

                AddTriangle(sideNormal, apex, v1, v2, sideColor);
            }
        }
    }
}
