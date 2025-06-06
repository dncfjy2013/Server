using OpenTK.Mathematics;
using StlGenerator.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StlGenerator.Shapes.MatrixGenerator
{
    /// <summary>
    /// 长方体矩阵生成器
    /// </summary>
    public class CuboidMatrixGenerator : MatrixGenerator
    {
        private readonly float _length;
        private readonly float _width;
        private readonly float _height;

        public CuboidMatrixGenerator(int rows, int columns, float length, float width, float height, float spacing = 0.1f)
            : base(rows, columns, spacing)
        {
            _length = length;
            _width = width;
            _height = height;
        }

        public override string ShapeName => "CuboidMatrix";

        protected override ShapeGenerator CreateElementGenerator(int row, int col)
        {
            return new CuboidGenerator(_length, _width, _height);
        }

        protected override Vector3 CalculateElementPosition(int row, int col, float elementSizeX, float elementSizeY)
        {
            float x = col * (elementSizeX + _spacing);
            float y = row * (elementSizeY + _spacing);
            return new Vector3(x, y, 0);
        }
    }
}
