using OpenTK.Mathematics;
using StlGenerator.Core;
using StlGenerator.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StlGenerator.Shapes.MatrixGenerator
{
    /// <summary>
    /// 抽象矩阵生成器
    /// </summary>
    public abstract class MatrixGenerator : ShapeGenerator
    {
        protected readonly int _rows;
        protected readonly int _columns;
        protected readonly float _spacing;

        public MatrixGenerator(int rows, int columns, float spacing = 0.1f)
        {
            _rows = rows;
            _columns = columns;
            _spacing = spacing;
        }

        /// <summary>
        /// 创建单个元素的生成器
        /// </summary>
        protected abstract ShapeGenerator CreateElementGenerator(int row, int col);

        /// <summary>
        /// 计算元素在矩阵中的位置
        /// </summary>
        protected abstract Vector3 CalculateElementPosition(int row, int col, float elementSizeX, float elementSizeY);

        protected override void GenerateShapeGeometry()
        {
            _model = new Model();

            for (int row = 0; row < _rows; row++)
            {
                for (int col = 0; col < _columns; col++)
                {
                    // 创建元素生成器
                    var elementGenerator = CreateElementGenerator(row, col);

                    // 生成元素模型
                    Model elementModel = elementGenerator.GenerateModel();

                    // 获取元素尺寸
                    BoundingBox boundingBox = elementModel.CalculateBoundingBox();
                    float elementSizeX = boundingBox.Max.X - boundingBox.Min.X;
                    float elementSizeY = boundingBox.Max.Y - boundingBox.Min.Y;

                    // 计算元素位置
                    Vector3 position = CalculateElementPosition(row, col, elementSizeX, elementSizeY);

                    // 应用变换
                    ApplyTransformation(elementModel, position);

                    // 将变换后的三角形添加到矩阵模型中
                    _model.Triangles.AddRange(elementModel.Triangles);
                }
            }
        }

        private void ApplyTransformation(Model model, Vector3 position)
        {
            // 创建平移矩阵
            Matrix4 translation = Matrix4.CreateTranslation(position);

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
