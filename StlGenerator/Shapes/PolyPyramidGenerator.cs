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
    /// 多棱锥生成器
    /// </summary>
    public class PolyPyramidGenerator : ShapeGenerator
    {
        private readonly int _sides;
        private readonly float _radius;
        private readonly float _height;

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
            // 生成底面
            GenerateBase();

            // 生成侧面
            GenerateSides();
        }

        private void GenerateBase()
        {
            Vector3 normal = -Vector3.UnitZ; // 底面法向量朝下
            Color4 color = GenerateColorBasedOnNormal(normal);

            // 计算多边形顶点
            Vector3[] vertices = new Vector3[_sides];
            for (int i = 0; i < _sides; i++)
            {
                float angle = i * 2 * MathHelper.Pi / _sides;
                vertices[i] = new Vector3(
                    _radius * (float)Math.Cos(angle),
                    _radius * (float)Math.Sin(angle),
                    0
                );
            }

            // 生成底面扇形三角形
            for (int i = 0; i < _sides; i++)
            {
                int nextIndex = (i + 1) % _sides;
                AddTriangle(normal, Vector3.Zero, vertices[i], vertices[nextIndex], color);
            }
        }

        private void GenerateSides()
        {
            // 定义顶点位置
            Vector3 apex = new Vector3(0, 0, _height);

            // 计算多边形顶点
            Vector3[] baseVertices = new Vector3[_sides];
            for (int i = 0; i < _sides; i++)
            {
                float angle = i * 2 * MathHelper.Pi / _sides;
                baseVertices[i] = new Vector3(
                    _radius * (float)Math.Cos(angle),
                    _radius * (float)Math.Sin(angle),
                    0
                );
            }

            // 生成侧面三角形
            for (int i = 0; i < _sides; i++)
            {
                int nextIndex = (i + 1) % _sides;

                // 计算侧面法向量
                Vector3 v1 = baseVertices[i];
                Vector3 v2 = baseVertices[nextIndex];
                Vector3 normal = Vector3.Normalize(Vector3.Cross(v1 - apex, v2 - apex));

                Color4 color = GenerateColorBasedOnNormal(normal);

                // 添加侧面三角形
                AddTriangle(normal, apex, v1, v2, color);
            }
        }
    }
}
