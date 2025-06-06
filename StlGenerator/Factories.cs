using OpenTK.Mathematics;
using StlGenerator.Core;
using StlGenerator.Models;
using StlGenerator.Rendering;
using StlGenerator.Shapes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StlGenerator
{
    /// <summary>
    /// 形状生成器工厂，用于创建不同类型的形状生成器
    /// </summary>
    public static class ShapeGeneratorFactory
    {
        public enum ShapeType
        {
            Cuboid,
            CuboidMatrix,
            Sphere,
            Cylinder,
            Cone,
            Pyramid
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

                case ShapeType.Cone:
                    if (parameters.Length >= 2 && parameters[0] is float radius2 && parameters[1] is float height3)
                    {
                        int sides = parameters.Length > 2 && parameters[2] is int ? (int)parameters[2] : 32;
                        return new ConeGenerator(radius2, height3, sides);
                    }
                    throw new ArgumentException("创建Cone需要半径和高度参数");

                case ShapeType.Pyramid:
                    if (parameters.Length >= 3 && parameters[0] is float baseWidth &&
                        parameters[1] is float baseLength && parameters[2] is float height4)
                    {
                        return new PyramidGenerator((float)parameters[0], (float)parameters[1], (float)parameters[2]);
                    }
                    throw new ArgumentException("创建Pyramid需要底面宽度、底面长度和高度参数");

                default:
                    throw new ArgumentException($"未知的形状类型: {shapeType}");
            }
        }

        /// <summary>
        /// 根据名称创建形状生成器实例
        /// </summary>
        public static IModelGenerator CreateGenerator(string shapeName, params object[] parameters)
        {
            if (Enum.TryParse(shapeName, true, out ShapeType shapeType))
            {
                return CreateGenerator(shapeType, parameters);
            }

            throw new ArgumentException($"未知的形状名称: {shapeName}");
        }

        /// <summary>
        /// 获取形状类型的描述信息
        /// </summary>
        public static string GetShapeDescription(ShapeType shapeType)
        {
            switch (shapeType)
            {
                case ShapeType.Cuboid:
                    return "长方体";
                case ShapeType.CuboidMatrix:
                    return "长方体矩阵";
                case ShapeType.Sphere:
                    return "球体";
                case ShapeType.Cylinder:
                    return "圆柱体";
                case ShapeType.Cone:
                    return "圆锥体";
                case ShapeType.Pyramid:
                    return "金字塔";
                default:
                    return "未知形状";
            }
        }

        /// <summary>
        /// 获取形状所需的参数信息
        /// </summary>
        public static string GetShapeParametersInfo(ShapeType shapeType)
        {
            switch (shapeType)
            {
                case ShapeType.Cuboid:
                    return "长度, 宽度, 高度";
                case ShapeType.CuboidMatrix:
                    return "行数, 列数, 长度, 宽度, 高度, [间距]";
                case ShapeType.Sphere:
                    return "半径, [切片数], [堆叠数]";
                case ShapeType.Cylinder:
                    return "半径, 高度, [边数]";
                case ShapeType.Cone:
                    return "半径, 高度, [边数]";
                case ShapeType.Pyramid:
                    return "底面宽度, 底面长度, 高度";
                default:
                    return "未知参数";
            }
        }

        /// <summary>
        /// 创建并渲染形状
        /// </summary>
        public static void CreateAndRenderShape(ShapeType shapeType, params object[] parameters)
        {
            IModelGenerator generator = CreateGenerator(shapeType, parameters);
            Model model = generator.GenerateModel();

            // 创建渲染器并显示模型
            using (StlViewer viewer = new StlViewer(model))
            {
                viewer.Title = GetShapeDescription(shapeType);
                viewer.Run();
            }
        }

        /// <summary>
        /// 创建形状并保存为STL文件，然后渲染
        /// </summary>
        public static void CreateSaveAndRenderShape(ShapeType shapeType, string filePath, params object[] parameters)
        {
            IModelGenerator generator = CreateGenerator(shapeType, parameters);
            generator.SaveToStl(filePath);

            // 从文件加载并渲染
            Parsers.StlParser parser = new Parsers.StlParser();
            Model model = parser.ParseStlFile(filePath);

            using (StlViewer viewer = new StlViewer(model))
            {
                viewer.Title = GetShapeDescription(shapeType);
                viewer.Run();
            }
        }

        /// <summary>
        /// 生成具有颜色的模型
        /// </summary>
        public static Model GenerateColoredModel(ShapeType shapeType, Color4 color, params object[] parameters)
        {
            IModelGenerator generator = CreateGenerator(shapeType, parameters);
            Model model = generator.GenerateModel();

            // 修复：获取三角形引用并修改
            for (int i = 0; i < model.Triangles.Count; i++)
            {
                // 获取当前三角形
                var triangle = model.Triangles[i];
                // 修改颜色
                triangle.Color = color;
                // 将修改后的三角形放回列表
                model.Triangles[i] = triangle;
            }

            return model;
        }

        /// <summary>
        /// 生成具有渐变颜色的模型
        /// </summary>
        public static Model GenerateGradientModel(ShapeType shapeType, Color4 startColor, Color4 endColor, params object[] parameters)
        {
            IModelGenerator generator = CreateGenerator(shapeType, parameters);
            Model model = generator.GenerateModel();

            if (model.Triangles.Count == 0)
                return model;

            // 修复：获取三角形引用并修改
            for (int i = 0; i < model.Triangles.Count; i++)
            {
                float t = (float)i / (model.Triangles.Count - 1);

                // 获取当前三角形
                var triangle = model.Triangles[i];
                // 修改颜色
                triangle.Color = new Color4(
                    MathHelper.Lerp(startColor.R, endColor.R, t),
                    MathHelper.Lerp(startColor.G, endColor.G, t),
                    MathHelper.Lerp(startColor.B, endColor.B, t),
                    MathHelper.Lerp(startColor.A, endColor.A, t)
                );
                // 将修改后的三角形放回列表
                model.Triangles[i] = triangle;
            }

            return model;
        }
    }
}
