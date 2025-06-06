using StlGenerator.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StlGenerator.Shapes
{
    /// <summary>
    /// 形状生成器工厂，用于创建不同类型的形状生成器
    /// </summary>
    public static class ShapeFactory
    {
        public enum ShapeType
        {
            Cuboid,
            CuboidMatrix,
            Sphere,
            Cylinder
        }

        /// <summary>
        /// 创建形状生成器实例
        /// </summary>
        public static IModelGenerator CreateGenerator(ShapeType shapeType, params object[] parameters)
        {
            switch (shapeType)
            {
                case ShapeType.Cuboid:
                    if (parameters.Length >= 3 && parameters[0] is float length &&
                        parameters[1] is float width && parameters[2] is float height)
                    {
                        return new CuboidGenerator(length, width, height);
                    }
                    throw new ArgumentException("创建Cuboid需要长度、宽度和高度参数");

                case ShapeType.CuboidMatrix:
                    if (parameters.Length >= 5 && parameters[0] is int rows &&
                        parameters[1] is int columns && parameters[2] is float length1 &&
                        parameters[3] is float width1 && parameters[4] is float height1)
                    {
                        float spacing = parameters.Length > 5 && parameters[5] is float ? (float)parameters[5] : 0.1f;
                        return new CuboidMatrixGenerator(rows, columns, length1, width1, height1, spacing);
                    }
                    throw new ArgumentException("创建CuboidMatrix需要行数、列数、长度、宽度和高度参数");

                case ShapeType.Sphere:
                    if (parameters.Length >= 1 && parameters[0] is float radius)
                    {
                        int slices = parameters.Length > 1 && parameters[1] is int ? (int)parameters[1] : 32;
                        int stacks = parameters.Length > 2 && parameters[2] is int ? (int)parameters[2] : 16;
                        return new SphereGenerator(radius, slices, stacks);
                    }
                    throw new ArgumentException("创建Sphere需要半径参数");

                case ShapeType.Cylinder:
                    if (parameters.Length >= 2 && parameters[0] is float radius1 && parameters[1] is float height2)
                    {
                        int sides = parameters.Length > 2 && parameters[2] is int ? (int)parameters[2] : 32;
                        return new CylinderGenerator(radius1, height2, sides);
                    }
                    throw new ArgumentException("创建Cylinder需要半径和高度参数");

                default:
                    throw new ArgumentException($"未知的形状类型: {shapeType}");
            }
        }
    }
}
