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
    /// 球体矩阵生成器
    /// </summary>
    public class SphereMatrixGenerator : MatrixGenerator
    {
        private readonly float _radius;
        private readonly int _slices;
        private readonly int _stacks;

        public SphereMatrixGenerator(int rows, int columns, float radius, float spacing = 0.1f, int slices = 32, int stacks = 16)
            : base(rows, columns, spacing)
        {
            _radius = radius;
            _slices = slices;
            _stacks = stacks;
        }

        public override string ShapeName => "SphereMatrix";

        protected override ShapeGenerator CreateElementGenerator(int row, int col)
        {
            return new SphereGenerator(_radius, _slices, _stacks);
        }

        protected override Vector3 CalculateElementPosition(int row, int col, float elementSizeX, float elementSizeY)
        {
            float x = col * (elementSizeX + _spacing);
            float y = row * (elementSizeY + _spacing);
            return new Vector3(x, y, _radius); // 球体放在地面上
        }
    }
}
