using OpenTK.Mathematics;
using StlGenerator.Core;
using StlGenerator.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StlGenerator.Shapes
{
    /// <summary>
    /// 长方体矩阵生成器
    /// </summary>
    public class CuboidMatrixGenerator : ShapeGenerator
    {
        private readonly int _rows;
        private readonly int _columns;
        private readonly float _length;
        private readonly float _width;
        private readonly float _height;
        private readonly float _spacing;
        private readonly CuboidGenerator _baseCuboidGenerator;

        public CuboidMatrixGenerator(int rows, int columns, float length, float width, float height, float spacing = 0.1f)
        {
            _rows = rows;
            _columns = columns;
            _length = length;
            _width = width;
            _height = height;
            _spacing = spacing;
            _baseCuboidGenerator = new CuboidGenerator(length, width, height);
        }

        public override string ShapeName => "CuboidMatrix";

        protected override void GenerateShapeGeometry()
        {
            _model = new Model();

            for (int row = 0; row < _rows; row++)
            {
                for (int col = 0; col < _columns; col++)
                {
                    float x = col * (_length + _spacing);
                    float y = row * (_width + _spacing);

                    // 生成单个长方体
                    Model cuboid = _baseCuboidGenerator.GenerateModel();

                    // 应用变换
                    ApplyTransformation(cuboid, x, y, 0);

                    // 将变换后的三角形添加到矩阵模型中
                    _model.Triangles.AddRange(cuboid.Triangles);
                }
            }
        }

        private void ApplyTransformation(Model model, float x, float y, float z)
        {
            // 创建平移矩阵
            Matrix4 translation = Matrix4.CreateTranslation(x, y, z);

            foreach (var triangle in model.Triangles)
            {
                // 变换顶点位置
                Vector4 v1 = new Vector4(triangle.Vertex1, 1.0f) * translation;
                Vector4 v2 = new Vector4(triangle.Vertex2, 1.0f) * translation;
                Vector4 v3 = new Vector4(triangle.Vertex3, 1.0f) * translation;

                // 计算新的法向量 (平移不影响法向量)
                Vector3 normal = triangle.Normal;

                // 添加变换后的三角形
                _model.Triangles.Add(new Triangle(
                    normal,
                    new Vector3(v1.X, v1.Y, v1.Z),
                    new Vector3(v2.X, v2.Y, v2.Z),
                    new Vector3(v3.X, v3.Y, v3.Z),
                    triangle.Color
                ));
            }
        }
    }
}
