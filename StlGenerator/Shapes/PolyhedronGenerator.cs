using OpenTK.Mathematics;
using StlGenerator.Core;
using StlGenerator.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StlGenerator.Shapes
{
    /// <summary>
    /// 多面体生成器，支持根据面的边数创建自定义多面体
    /// </summary>
    public class PolyhedronGenerator : ShapeGenerator
    {
        private readonly int _faces;          // 多面体面数
        private readonly int _edgesPerFace;   // 每个面的边数
        private readonly float _radius;       // 多面体半径
        private readonly bool _isRegular;     // 是否为正多面体

        public PolyhedronGenerator(int faces, int edgesPerFace, float radius = 1.0f, bool isRegular = true)
        {
            if (faces < 4 || edgesPerFace < 3)
                throw new ArgumentException("多面体面数至少为4，每个面边数至少为3");

            _faces = faces;
            _radius = radius;
            _isRegular = isRegular;
        }

        public override string ShapeName => $"{_faces}面体({_edgesPerFace}边/面)";

        protected override void GenerateShapeGeometry()
        {
            if (_isRegular)
            {
                GenerateRegularPolyhedron();
            }
            else
            {
                GenerateCustomPolyhedron();
            }
        }

        /// <summary>
        /// 生成正多面体（柏拉图立体）
        /// </summary>
        private void GenerateRegularPolyhedron()
        {
            // 正多面体顶点预计算（简化实现，仅支持部分正多面体）
            switch (_faces)
            {
                case 4:  // 正四面体（4个三角形面）
                    GenerateTetrahedron();
                    break;
                case 6:  // 正六面体（立方体，6个四边形面）
                    GenerateCube();
                    break;
                case 8:  // 正八面体（8个三角形面）
                    GenerateOctahedron();
                    break;
                case 12: // 正十二面体（12个五边形面）
                    GenerateDodecahedron();
                    break;
                case 20: // 正二十面体（20个三角形面）
                    GenerateIcosahedron();
                    break;
                default:
                    throw new NotSupportedException($"不支持的正多面体: {_faces}面体");
            }
        }

        /// <summary>
        /// 生成自定义多面体
        /// </summary>
        private void GenerateCustomPolyhedron()
        {
            // 简化实现：基于球面离散化生成近似多面体
            int stacks = _faces / 2;
            int slices = _edgesPerFace;

            for (int i = 0; i <= stacks; i++)
            {
                float stackAngle = MathHelper.PiOver2 - i * MathHelper.Pi / stacks;
                float y = _radius * (float)Math.Sin(stackAngle);
                float r = _radius * (float)Math.Cos(stackAngle);

                for (int j = 0; j < _edgesPerFace; j++)
                {
                    float sliceAngle1 = j * 2 * MathHelper.Pi / _edgesPerFace;
                    float sliceAngle2 = (j + 1) * 2 * MathHelper.Pi / _edgesPerFace;

                    // 计算当前面的顶点
                    Vector3 v1 = new Vector3(
                        r * (float)Math.Cos(sliceAngle1),
                        y,
                        r * (float)Math.Sin(sliceAngle1)
                    );

                    Vector3 v2 = new Vector3(
                        r * (float)Math.Cos(sliceAngle2),
                        y,
                        r * (float)Math.Sin(sliceAngle2)
                    );

                    // 计算下一层顶点
                    if (i < stacks)
                    {
                        float nextStackAngle = MathHelper.PiOver2 - (i + 1) * MathHelper.Pi / stacks;
                        float nextY = _radius * (float)Math.Sin(nextStackAngle);
                        float nextR = _radius * (float)Math.Cos(nextStackAngle);

                        Vector3 v3 = new Vector3(
                            nextR * (float)Math.Cos(sliceAngle2),
                            nextY,
                            nextR * (float)Math.Sin(sliceAngle2)
                        );

                        Vector3 v4 = new Vector3(
                            nextR * (float)Math.Cos(sliceAngle1),
                            nextY,
                            nextR * (float)Math.Sin(sliceAngle1)
                        );

                        // 生成三角形面
                        Vector3 normal1 = Vector3.Normalize(v1);
                        Vector3 normal2 = Vector3.Normalize(v3);

                        AddTriangle(normal1, v1, v2, v4, GenerateColorBasedOnNormal(normal1));
                        AddTriangle(normal2, v2, v3, v4, GenerateColorBasedOnNormal(normal2));
                    }
                }
            }
        }

        // 正多面体生成方法（以正四面体为例）
        private void GenerateTetrahedron()
        {
            // 正四面体顶点坐标（内接于半径为radius的球）
            float factor = (float)(_radius / MathHelper.Sqrt(6) * 2);
            Vector3 v0 = new Vector3(-factor, -factor, factor);
            Vector3 v1 = new Vector3(-factor, factor, -factor);
            Vector3 v2 = new Vector3(factor, -factor, -factor);
            Vector3 v3 = new Vector3(factor, factor, factor);

            // 面1：v0, v1, v2
            Vector3 normal1 = Vector3.Normalize(Vector3.Cross(v1 - v0, v2 - v0));
            AddTriangle(normal1, v0, v1, v2, GenerateColorBasedOnNormal(normal1));

            // 面2：v0, v2, v3
            Vector3 normal2 = Vector3.Normalize(Vector3.Cross(v2 - v0, v3 - v0));
            AddTriangle(normal2, v0, v2, v3, GenerateColorBasedOnNormal(normal2));

            // 面3：v0, v3, v1
            Vector3 normal3 = Vector3.Normalize(Vector3.Cross(v3 - v0, v1 - v0));
            AddTriangle(normal3, v0, v3, v1, GenerateColorBasedOnNormal(normal3));

            // 面4：v1, v3, v2
            Vector3 normal4 = Vector3.Normalize(Vector3.Cross(v3 - v1, v2 - v1));
            AddTriangle(normal4, v1, v3, v2, GenerateColorBasedOnNormal(normal4));
        }

        // 正六面体生成方法
        private void GenerateCube()
        {
            float halfSize = (float)(_radius / MathHelper.Sqrt(3));
            Vector3 v0 = new Vector3(-halfSize, -halfSize, -halfSize);
            Vector3 v1 = new Vector3(halfSize, -halfSize, -halfSize);
            Vector3 v2 = new Vector3(halfSize, halfSize, -halfSize);
            Vector3 v3 = new Vector3(-halfSize, halfSize, -halfSize);
            Vector3 v4 = new Vector3(-halfSize, -halfSize, halfSize);
            Vector3 v5 = new Vector3(halfSize, -halfSize, halfSize);
            Vector3 v6 = new Vector3(halfSize, halfSize, halfSize);
            Vector3 v7 = new Vector3(-halfSize, halfSize, halfSize);

            // 前后面
            AddTriangle(Vector3.UnitZ, v0, v1, v2, GenerateColorBasedOnNormal(Vector3.UnitZ));
            AddTriangle(Vector3.UnitZ, v0, v2, v3, GenerateColorBasedOnNormal(Vector3.UnitZ));
            AddTriangle(-Vector3.UnitZ, v4, v5, v6, GenerateColorBasedOnNormal(-Vector3.UnitZ));
            AddTriangle(-Vector3.UnitZ, v4, v6, v7, GenerateColorBasedOnNormal(-Vector3.UnitZ));

            // 左右面
            AddTriangle(-Vector3.UnitX, v0, v3, v7, GenerateColorBasedOnNormal(-Vector3.UnitX));
            AddTriangle(-Vector3.UnitX, v0, v7, v4, GenerateColorBasedOnNormal(-Vector3.UnitX));
            AddTriangle(Vector3.UnitX, v1, v5, v6, GenerateColorBasedOnNormal(Vector3.UnitX));
            AddTriangle(Vector3.UnitX, v1, v6, v2, GenerateColorBasedOnNormal(Vector3.UnitX));

            // 上下面
            AddTriangle(Vector3.UnitY, v3, v2, v6, GenerateColorBasedOnNormal(Vector3.UnitY));
            AddTriangle(Vector3.UnitY, v3, v6, v7, GenerateColorBasedOnNormal(Vector3.UnitY));
            AddTriangle(-Vector3.UnitY, v0, v4, v5, GenerateColorBasedOnNormal(-Vector3.UnitY));
            AddTriangle(-Vector3.UnitY, v0, v5, v1, GenerateColorBasedOnNormal(-Vector3.UnitY));
        }

        // 正八面体生成方法
        private void GenerateOctahedron()
        {
            // 正八面体顶点坐标（内接于半径为radius的球）
            Vector3[] vertices = new Vector3[6]
            {
                new Vector3(_radius, 0, 0),       // 右
                new Vector3(-_radius, 0, 0),      // 左
                new Vector3(0, _radius, 0),       // 上
                new Vector3(0, -_radius, 0),      // 下
                new Vector3(0, 0, _radius),       // 前
                new Vector3(0, 0, -_radius)       // 后
            };

            // 定义8个面的顶点索引
            int[][] faces = new int[8][]
            {
                new int[] { 0, 2, 4 },  // 右面-上-前
                new int[] { 0, 4, 3 },  // 右面-前-下
                new int[] { 0, 3, 5 },  // 右面-下-后
                new int[] { 0, 5, 2 },  // 右面-后-上
                new int[] { 1, 2, 5 },  // 左面-上-后
                new int[] { 1, 5, 3 },  // 左面-后-下
                new int[] { 1, 3, 4 },  // 左面-下-前
                new int[] { 1, 4, 2 }   // 左面-前-上
            };

            // 生成8个三角形面
            for (int i = 0; i < 8; i++)
            {
                Vector3 v1 = vertices[faces[i][0]];
                Vector3 v2 = vertices[faces[i][1]];
                Vector3 v3 = vertices[faces[i][2]];

                Vector3 normal = Vector3.Normalize(Vector3.Cross(v2 - v1, v3 - v1));
                AddTriangle(normal, v1, v2, v3, GenerateColorBasedOnNormal(normal));
            }
        }

        // 正十二面体生成方法
        private void GenerateDodecahedron()
        {
            // 正十二面体的黄金比例
            float phi = (1 + (float)Math.Sqrt(5)) / 2;
            float a = _radius / phi;
            float b = _radius;
            float c = _radius / (phi * phi);

            // 正十二面体顶点坐标（内接于半径为radius的球）
            Vector3[] vertices = new Vector3[20]
            {
                new Vector3(0, a, b), new Vector3(0, a, -b), new Vector3(0, -a, b), new Vector3(0, -a, -b),
                new Vector3(a, b, 0), new Vector3(a, -b, 0), new Vector3(-a, b, 0), new Vector3(-a, -b, 0),
                new Vector3(b, 0, a), new Vector3(b, 0, -a), new Vector3(-b, 0, a), new Vector3(-b, 0, -a),
                new Vector3(c, c, c), new Vector3(c, c, -c), new Vector3(c, -c, c), new Vector3(c, -c, -c),
                new Vector3(-c, c, c), new Vector3(-c, c, -c), new Vector3(-c, -c, c), new Vector3(-c, -c, -c)
            };

            // 定义12个面的顶点索引
            int[][] faces = new int[12][]
            {
                new int[] { 0, 12, 8, 14, 2 },
                new int[] { 1, 13, 9, 15, 3 },
                new int[] { 4, 12, 13, 6, 0 },
                new int[] { 5, 14, 15, 7, 2 },
                new int[] { 8, 12, 4, 10, 16 },
                new int[] { 9, 13, 5, 11, 17 },
                new int[] { 10, 14, 5, 8, 18 },
                new int[] { 11, 15, 4, 9, 19 },
                new int[] { 0, 6, 17, 1, 13 },
                new int[] { 2, 7, 19, 3, 15 },
                new int[] { 6, 16, 18, 7, 19 },
                new int[] { 10, 16, 0, 8, 12 }
            };

            // 生成12个面，每个面都是五边形，分解为3个三角形
            for (int i = 0; i < 12; i++)
            {
                Vector3 v1 = vertices[faces[i][0]];
                Vector3 v2 = vertices[faces[i][1]];
                Vector3 v3 = vertices[faces[i][2]];
                Vector3 v4 = vertices[faces[i][3]];
                Vector3 v5 = vertices[faces[i][4]];

                // 计算面的法向量
                Vector3 normal = Vector3.Normalize(Vector3.Cross(v2 - v1, v3 - v1));

                // 将五边形分解为3个三角形
                AddTriangle(normal, v1, v2, v3, GenerateColorBasedOnNormal(normal));
                AddTriangle(normal, v1, v3, v4, GenerateColorBasedOnNormal(normal));
                AddTriangle(normal, v1, v4, v5, GenerateColorBasedOnNormal(normal));
            }
        }

        // 正二十面体生成方法
        private void GenerateIcosahedron()
        {
            // 正二十面体的黄金比例
            float phi = (1 + (float)Math.Sqrt(5)) / 2;
            float r = (float)(_radius / MathHelper.Sqrt(1 + phi * phi)); // 缩放因子，确保顶点在球面上

            // 正二十面体顶点坐标（内接于半径为radius的球）
            Vector3[] vertices = new Vector3[12]
            {
                new Vector3(0, r, r * phi), new Vector3(0, r, -r * phi), new Vector3(0, -r, r * phi),
                new Vector3(0, -r, -r * phi), new Vector3(r, r * phi, 0), new Vector3(r, -r * phi, 0),
                new Vector3(-r, r * phi, 0), new Vector3(-r, -r * phi, 0), new Vector3(r * phi, 0, r),
                new Vector3(r * phi, 0, -r), new Vector3(-r * phi, 0, r), new Vector3(-r * phi, 0, -r)
            };

            // 定义20个面的顶点索引
            int[][] faces = new int[20][]
            {
                new int[] { 0, 4, 6 }, new int[] { 0, 6, 10 }, new int[] { 0, 10, 8 }, new int[] { 0, 8, 4 },
                new int[] { 1, 6, 4 }, new int[] { 1, 11, 6 }, new int[] { 1, 9, 11 }, new int[] { 1, 4, 9 },
                new int[] { 2, 10, 7 }, new int[] { 2, 7, 5 }, new int[] { 2, 5, 8 }, new int[] { 2, 8, 10 },
                new int[] { 3, 9, 5 }, new int[] { 3, 5, 7 }, new int[] { 3, 7, 11 }, new int[] { 3, 11, 9 },
                new int[] { 4, 8, 5 }, new int[] { 4, 5, 9 }, new int[] { 6, 11, 7 }, new int[] { 6, 7, 10 }
            };

            // 生成20个三角形面
            for (int i = 0; i < 20; i++)
            {
                Vector3 v1 = vertices[faces[i][0]];
                Vector3 v2 = vertices[faces[i][1]];
                Vector3 v3 = vertices[faces[i][2]];

                Vector3 normal = Vector3.Normalize(Vector3.Cross(v2 - v1, v3 - v1));
                AddTriangle(normal, v1, v2, v3, GenerateColorBasedOnNormal(normal));
            }
        }
    }
}