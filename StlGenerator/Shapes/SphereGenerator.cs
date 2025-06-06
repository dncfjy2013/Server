using OpenTK.Mathematics;
using StlGenerator.Core;
using System;
using System.Collections.Generic;

namespace StlGenerator.Shapes
{
    /// <summary>
    /// 球体生成器（Y轴向上，右手坐标系）
    /// </summary>
    public class SphereGenerator : ShapeGenerator
    {
        private readonly float _radius;     // 球体半径
        private readonly int _slices;       // 水平分段数
        private readonly int _stacks;       // 垂直分段数

        public SphereGenerator(float radius, int slices = 32, int stacks = 16)
        {
            if (slices < 3 || stacks < 3)
                throw new ArgumentException("分段数必须至少为3", nameof(slices) + "/" + nameof(stacks));

            _radius = radius;
            _slices = slices;
            _stacks = stacks;
        }

        public override string ShapeName => "Sphere";

        protected override void GenerateShapeGeometry()
        {
            for (int i = 0; i <= _stacks; i++)
            {
                // 计算垂直方向角度（Y轴为垂直方向）
                float stackAngle = MathHelper.PiOver2 - i * MathHelper.Pi / _stacks;
                float y = _radius * (float)Math.Sin(stackAngle);         // Y坐标
                float r = _radius * (float)Math.Cos(stackAngle);         // XZ平面半径

                for (int j = 0; j <= _slices; j++)
                {
                    // 计算水平方向角度（XZ平面）
                    float sliceAngle = j * 2 * MathHelper.Pi / _slices;
                    float x = r * (float)Math.Cos(sliceAngle);           // X坐标
                    float z = r * (float)Math.Sin(sliceAngle);           // Z坐标

                    Vector3 vertex = new Vector3(x, y, z);
                    Vector3 normal = Vector3.Normalize(vertex);
                    Color4 color = GenerateColorBasedOnNormal(normal);

                    // 索引计算（用于调试）
                    int current = i * (_slices + 1) + j;
                    int next = (i + 1) * (_slices + 1) + j;

                    if (i < _stacks && j < _slices)
                    {
                        // 计算四个顶点（Y轴向上）
                        Vector3 v1 = vertex;
                        Vector3 v2 = new Vector3(
                            r * (float)Math.Cos(sliceAngle + 2 * MathHelper.Pi / _slices),
                            y,
                            r * (float)Math.Sin(sliceAngle + 2 * MathHelper.Pi / _slices)
                        );

                        // 下一层顶点
                        float nextStackAngle = MathHelper.PiOver2 - (i + 1) * MathHelper.Pi / _stacks;
                        float nextY = _radius * (float)Math.Sin(nextStackAngle);
                        float nextR = _radius * (float)Math.Cos(nextStackAngle);

                        Vector3 v3 = new Vector3(
                            nextR * (float)Math.Cos(sliceAngle + 2 * MathHelper.Pi / _slices),
                            nextY,
                            nextR * (float)Math.Sin(sliceAngle + 2 * MathHelper.Pi / _slices)
                        );
                        Vector3 v4 = new Vector3(
                            nextR * (float)Math.Cos(sliceAngle),
                            nextY,
                            nextR * (float)Math.Sin(sliceAngle)
                        );

                        // 计算法向量（球面法向量指向外侧）
                        Vector3 normal1 = Vector3.Normalize(v1);
                        Vector3 normal2 = Vector3.Normalize(v2);
                        Vector3 normal3 = Vector3.Normalize(v3);
                        Vector3 normal4 = Vector3.Normalize(v4);

                        // 生成两个三角形（右手坐标系缠绕顺序）
                        AddTriangle(normal1, v1, v2, v4, color);
                        AddTriangle(normal3, v2, v3, v4, color);
                    }
                }
            }
        }
    }
}