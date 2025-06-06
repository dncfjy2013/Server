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
    /// 球体生成器
    /// </summary>
    public class SphereGenerator : ShapeGenerator
    {
        private readonly float _radius;
        private readonly int _slices;
        private readonly int _stacks;

        public SphereGenerator(float radius, int slices = 32, int stacks = 16)
        {
            _radius = radius;
            _slices = slices;
            _stacks = stacks;
        }

        public override string ShapeName => "Sphere";

        protected override void GenerateShapeGeometry()
        {
            for (int i = 0; i <= _stacks; i++)
            {
                float stackAngle = MathHelper.PiOver2 - i * MathHelper.Pi / _stacks;
                float y0 = _radius * (float)Math.Sin(stackAngle);
                float r0 = _radius * (float)Math.Cos(stackAngle);

                for (int j = 0; j <= _slices; j++)
                {
                    float sliceAngle = j * 2 * MathHelper.Pi / _slices;
                    float x0 = r0 * (float)Math.Cos(sliceAngle);
                    float z0 = r0 * (float)Math.Sin(sliceAngle);

                    Vector3 vertex = new Vector3(x0, y0, z0);
                    Vector3 normal = Vector3.Normalize(vertex);

                    // 为每个顶点添加颜色
                    Color4 color = GenerateColorBasedOnNormal(normal);

                    // 索引计算
                    int current = i * (_slices + 1) + j;
                    int next = (i + 1) * (_slices + 1) + j;

                    if (i < _stacks && j < _slices)
                    {
                        // 计算四个顶点
                        Vector3 v1 = vertex;
                        Vector3 v2 = new Vector3(
                            r0 * (float)Math.Cos(sliceAngle + 2 * MathHelper.Pi / _slices),
                            y0,
                            r0 * (float)Math.Sin(sliceAngle + 2 * MathHelper.Pi / _slices)
                        );

                        float nextStackAngle = MathHelper.PiOver2 - (i + 1) * MathHelper.Pi / _stacks;
                        float y1 = _radius * (float)Math.Sin(nextStackAngle);
                        float r1 = _radius * (float)Math.Cos(nextStackAngle);

                        Vector3 v3 = new Vector3(
                            r1 * (float)Math.Cos(sliceAngle + 2 * MathHelper.Pi / _slices),
                            y1,
                            r1 * (float)Math.Sin(sliceAngle + 2 * MathHelper.Pi / _slices)
                        );
                        Vector3 v4 = new Vector3(
                            r1 * (float)Math.Cos(sliceAngle),
                            y1,
                            r1 * (float)Math.Sin(sliceAngle)
                        );

                        // 计算法向量
                        Vector3 normal1 = Vector3.Normalize(v1);
                        Vector3 normal2 = Vector3.Normalize(v2);
                        Vector3 normal3 = Vector3.Normalize(v3);
                        Vector3 normal4 = Vector3.Normalize(v4);

                        // 生成两个三角形
                        AddTriangle(normal1, v1, v2, v4, color);
                        AddTriangle(normal3, v2, v3, v4, color);
                    }
                }
            }
        }
    }
}
