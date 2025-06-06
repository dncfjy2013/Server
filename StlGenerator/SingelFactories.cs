using OpenTK.Mathematics;
using StlGenerator.Core;
using StlGenerator.Models;
using StlGenerator.Rendering;
using StlGenerator.Shapes;
using StlGenerator.Shapes.MatrixGenerator;
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
            Cuboid = 0x0000,
            Sphere = 0x0001,
            Cylinder = 0x0002,
            Cone = 0x0003,
            Pyramid = 0x0004,
            Ellipse = 0x0005,
            Prism = 0x0006,  // 新增多棱柱形状

            CuboidMatrix = 0x00FF,
            SphereMatrix = 0x01FF,
        }

        /// <summary>
        /// 创建形状生成器实例
        /// </summary>
        public static IModelGenerator CreateGenerator(ShapeType shapeType, params object[] parameters)
        {
            switch (shapeType)
            {
                case ShapeType.Cuboid:
                    if (parameters.Length >= 3 && parameters[0] is float Cuboidlength &&
                        parameters[1] is float Cuboidwidth && parameters[2] is float Cuboidheight)
                    {
                        return new CuboidGenerator(Cuboidlength, Cuboidwidth, Cuboidheight);
                    }
                    throw new ArgumentException("创建Cuboid需要长度、宽度和高度参数");

                case ShapeType.Sphere:
                    if (parameters.Length >= 1 && parameters[0] is float Sphereradius)
                    {
                        int Sphereslices = parameters.Length > 1 && parameters[1] is int ? (int)parameters[1] : 32;
                        int Spherestacks = parameters.Length > 2 && parameters[2] is int ? (int)parameters[2] : 16;
                        return new SphereGenerator(Sphereradius, Sphereslices, Spherestacks);
                    }
                    throw new ArgumentException("创建Sphere需要半径参数");

                case ShapeType.Cylinder:
                    if (parameters.Length >= 2 && parameters[0] is float Cylinderradius && parameters[1] is float Cylinderheight)
                    {
                        int Cylindersides = parameters.Length > 2 && parameters[2] is int ? (int)parameters[2] : 32;
                        return new CylinderGenerator(Cylinderradius, Cylinderheight, Cylindersides);
                    }
                    throw new ArgumentException("创建Cylinder需要半径和高度参数");

                case ShapeType.Cone:
                    if (parameters.Length >= 2 && parameters[0] is float Coneradius && parameters[1] is float Coneheight)
                    {
                        int Conesides = parameters.Length > 2 && parameters[2] is int ? (int)parameters[2] : 32;
                        return new ConeGenerator(Coneradius, Coneheight, Conesides);
                    }
                    throw new ArgumentException("创建Cone需要半径和高度参数");

                case ShapeType.Pyramid:
                    if (parameters.Length >= 3 && parameters[0] is float PyramidbaseWidth &&
                        parameters[1] is float PyramidbaseLength && parameters[2] is float Pyramidheight)
                    {
                        return new PyramidGenerator(PyramidbaseWidth, PyramidbaseLength, Pyramidheight);
                    }
                    throw new ArgumentException("创建Pyramid需要底面宽度、底面长度和高度参数");

                case ShapeType.Ellipse:
                    if (parameters.Length >= 2 && parameters[0] is float EllipseradiusX && parameters[1] is float EllipseradiusY)
                    {
                        float Ellipsethickness = parameters.Length > 2 && parameters[2] is float ? (float)parameters[2] : 0.1f;
                        int Ellipsesegments = parameters.Length > 3 && parameters[3] is int ? (int)parameters[3] : 32;
                        return new EllipseGenerator(EllipseradiusX, EllipseradiusY, Ellipsethickness, Ellipsesegments);
                    }
                    throw new ArgumentException("创建Ellipse需要X半径和Y半径参数");

                case ShapeType.Prism:
                    if (parameters.Length >= 3 && parameters[0] is int Prismsides &&
                        parameters[1] is float Prismradius && parameters[2] is float Prismheight)
                    {
                        return new PrismGenerator(Prismsides, Prismradius, Prismheight);
                    }
                    throw new ArgumentException("创建Prism需要边数、底面半径和高度参数");

                case ShapeType.CuboidMatrix:
                    if (parameters.Length >= 5 && parameters[0] is int CuboidMatrixrows &&
                        parameters[1] is int CuboidMatrixcolumns && parameters[2] is float CuboidMatrixlength &&
                        parameters[3] is float CuboidMatrixwidth && parameters[4] is float CuboidMatrixheight)
                    {
                        float CuboidMatrixspacing = parameters.Length > 5 && parameters[5] is float ? (float)parameters[5] : 0.1f;
                        return new CuboidMatrixGenerator(CuboidMatrixrows, CuboidMatrixcolumns, CuboidMatrixlength, CuboidMatrixwidth, CuboidMatrixheight, CuboidMatrixspacing);
                    }
                    throw new ArgumentException("创建CuboidMatrix需要行数、列数、长度、宽度和高度参数");

                case ShapeType.SphereMatrix:
                    if (parameters.Length >= 3 && parameters[0] is int SphereMatrixrows &&
                        parameters[1] is int SphereMatrixcolumns && parameters[2] is float SphereMatrixradius)
                    {
                        float SphereMatrixspacing = parameters.Length > 3 && parameters[3] is float ? (float)parameters[3] : 0.1f;
                        int SphereMatrixslices = parameters.Length > 4 && parameters[4] is int ? (int)parameters[4] : 32;
                        int SphereMatrixstacks = parameters.Length > 5 && parameters[5] is int ? (int)parameters[5] : 16;
                        return new SphereMatrixGenerator(SphereMatrixrows, SphereMatrixcolumns, SphereMatrixradius, SphereMatrixspacing, SphereMatrixslices, SphereMatrixstacks);
                    }
                    throw new ArgumentException("创建SphereMatrix需要行数、列数和半径参数");

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
                case ShapeType.Ellipse:
                    return "椭圆";
                case ShapeType.Prism:
                    return "多棱柱";
                case ShapeType.SphereMatrix:
                    return "球体矩阵";
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
                case ShapeType.Ellipse:
                    return "X半径, Y半径, [厚度], [分段数]";
                case ShapeType.Prism:
                    return "边数, 底面半径, 高度";
                case ShapeType.SphereMatrix:
                    return "行数, 列数, 半径, [间距], [切片数], [堆叠数]";
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
