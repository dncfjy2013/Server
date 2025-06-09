using StlGenerator.Core;
using StlGenerator.Rendering;
using OpenTK.Mathematics;
using System;
using StlGenerator.Models;
using System.Xml;
using StlGenerator.Parsers;
using StlGenerator;

// 使用组合形状工厂方法创建房子
Model houseModel = ShapeGeneratorFactory.CreateCombinedShape(
    ShapeGeneratorFactory.CombinedShapeType.House,
    3.0f,  // 房子长度
    2.0f,  // 房子宽度
    1.5f,  // 房子高度
    1.0f   // 屋顶高度
);

StlFileWriter.SaveModelToStl(houseModel, "factory_house.stl", true);

using (StlViewer viewer = new StlViewer(houseModel))
{
    viewer.Title = "工厂生成的房子模型";
    viewer.Run();
}

namespace StlGenerator
{
    class Program
    {
        static void Main(string[] args)
        {
            InteractiveMain();
            try
            {
                // 1. 直接创建并渲染长方体
                ShapeGeneratorFactory.CreateAndRenderShape(
                    ShapeGeneratorFactory.ShapeType.Cuboid,
                    2.0f, 1.5f, 0.8f  // 长度、宽度、高度
                );

                // 2. 创建并渲染彩色球体
                Model coloredSphere = ShapeGeneratorFactory.GenerateColoredModel(
                    ShapeGeneratorFactory.ShapeType.Sphere,
                    Color4.Cyan,      // 球体颜色
                    1.2f, 32, 16      // 半径、切片数、堆叠数
                );

                using (StlViewer viewer = new StlViewer(coloredSphere))
                {
                    viewer.Title = "彩色球体";
                    viewer.Run();
                }

                // 3. 创建渐变颜色的长方体矩阵并保存为STL
                Model gradientMatrix = ShapeGeneratorFactory.GenerateGradientModel(
                    ShapeGeneratorFactory.ShapeType.CuboidMatrix,
                    Color4.Red,       // 起始颜色
                    Color4.Blue,      // 结束颜色
                    3, 4,             // 3行4列
                    1.0f, 0.8f,       // 单个长方体尺寸
                    0.5f,             // 高度
                    0.2f              // 间距
                );

                StlFileWriter.SaveModelToStl(gradientMatrix ,"gradient_matrix.stl", true);
                Console.WriteLine("渐变矩阵STL文件已生成");

                // 4. 创建圆柱体，保存为文件，然后渲染
                ShapeGeneratorFactory.CreateSaveAndRenderShape(
                    ShapeGeneratorFactory.ShapeType.Cylinder,
                    "cylinder.stl",
                    0.7f, 2.5f, 24    // 半径、高度、边数
                );

                Console.WriteLine("所有操作完成！");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"错误: {ex.Message}");
            }
        }

        // 交互式调用示例
        static void InteractiveMain()
        {
            Console.WriteLine("STL模型生成器");
            Console.WriteLine("----------------");

            // 选择操作类型
            Console.WriteLine("请选择操作:");
            Console.WriteLine("1. 生成并渲染模型");
            Console.WriteLine("2. 生成、保存并渲染模型");
            Console.WriteLine("3. 生成彩色模型");
            Console.WriteLine("4. 生成渐变模型");
            int operation = int.Parse(Console.ReadLine());

            // 选择形状类型
            // 交互式菜单选择形状类型
            Console.WriteLine("请选择形状类型:");
            Console.WriteLine("1. 长方体");
            Console.WriteLine("2. 球体");
            Console.WriteLine("3. 圆柱体");
            Console.WriteLine("4. 圆锥体");
            Console.WriteLine("5. 金字塔");
            Console.WriteLine("6. 椭圆");
            Console.WriteLine("7. 多棱柱"); 
            Console.WriteLine("8. 多棱锥");  // 新增多棱锥选项

            Console.WriteLine("100. 长方体矩阵");
            Console.WriteLine("101. 球体矩阵");
            int shapeChoice = int.Parse(Console.ReadLine());

            // 根据用户选择映射到正确的枚举值
            ShapeGeneratorFactory.ShapeType shapeType = shapeChoice switch
            {
                1 => ShapeGeneratorFactory.ShapeType.Cuboid,
                2 => ShapeGeneratorFactory.ShapeType.Sphere,
                3 => ShapeGeneratorFactory.ShapeType.Cylinder,
                4 => ShapeGeneratorFactory.ShapeType.Cone,
                5 => ShapeGeneratorFactory.ShapeType.Pyramid,
                6 => ShapeGeneratorFactory.ShapeType.Ellipse,
                7 => ShapeGeneratorFactory.ShapeType.Prism,
                8 => ShapeGeneratorFactory.ShapeType.PolyPyramid,

                100 => ShapeGeneratorFactory.ShapeType.CuboidMatrix,
                101 => ShapeGeneratorFactory.ShapeType.SphereMatrix,
                _ => throw new ArgumentException("无效的形状选择")
            };

            try
            {
                switch (operation)
                {
                    case 1: // 生成并渲染
                        object[] parameters = GetShapeParameters(shapeType);
                        ShapeGeneratorFactory.CreateAndRenderShape(shapeType, parameters);
                        break;

                    case 2: // 生成、保存并渲染
                        Console.Write("输入保存文件名: ");
                        string fileName = Console.ReadLine();
                        parameters = GetShapeParameters(shapeType);
                        ShapeGeneratorFactory.CreateSaveAndRenderShape(shapeType, fileName, parameters);
                        break;

                    case 3: // 生成彩色模型
                        Console.WriteLine("输入颜色值 (R,G,B,A 0-1之间的浮点数):");
                        string[] colorParts = Console.ReadLine().Split(',');
                        Color4 color = new Color4(
                            float.Parse(colorParts[0]),
                            float.Parse(colorParts[1]),
                            float.Parse(colorParts[2]),
                            float.Parse(colorParts[3])
                        );

                        parameters = GetShapeParameters(shapeType);
                        Model coloredModel = ShapeGeneratorFactory.GenerateColoredModel(shapeType, color, parameters);

                        using (StlViewer viewer = new StlViewer(coloredModel))
                        {
                            viewer.Title = $"{ShapeGeneratorFactory.GetShapeDescription(shapeType)} - 彩色";
                            viewer.Run();
                        }
                        break;

                    case 4: // 生成渐变模型
                        Console.WriteLine("输入起始颜色 (R,G,B,A):");
                        colorParts = Console.ReadLine().Split(',');
                        Color4 startColor = new Color4(
                            float.Parse(colorParts[0]),
                            float.Parse(colorParts[1]),
                            float.Parse(colorParts[2]),
                            float.Parse(colorParts[3])
                        );

                        Console.WriteLine("输入结束颜色 (R,G,B,A):");
                        colorParts = Console.ReadLine().Split(',');
                        Color4 endColor = new Color4(
                            float.Parse(colorParts[0]),
                            float.Parse(colorParts[1]),
                            float.Parse(colorParts[2]),
                            float.Parse(colorParts[3])
                        );

                        parameters = GetShapeParameters(shapeType);
                        Model gradientModel = ShapeGeneratorFactory.GenerateGradientModel(shapeType, startColor, endColor, parameters);

                        using (StlViewer viewer = new StlViewer(gradientModel))
                        {
                            viewer.Title = $"{ShapeGeneratorFactory.GetShapeDescription(shapeType)} - 渐变";
                            viewer.Run();
                        }
                        break;

                    default:
                        Console.WriteLine("无效选择");
                        return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"错误: {ex.Message}");
            }
        }

        // 获取形状参数的辅助方法
        private static object[] GetShapeParameters(ShapeGeneratorFactory.ShapeType shapeType)
        {
            Console.WriteLine($"请输入{ShapeGeneratorFactory.GetShapeDescription(shapeType)}的参数 ({ShapeGeneratorFactory.GetShapeParametersInfo(shapeType)}):");
            string[] inputs = Console.ReadLine().Split(',');

            object[] parameters = new object[inputs.Length];
            for (int i = 0; i < inputs.Length; i++)
            {
                // 根据参数位置判断类型
                if (shapeType == ShapeGeneratorFactory.ShapeType.CuboidMatrix && i < 2)
                {
                    parameters[i] = int.Parse(inputs[i]); // 行数和列数为整数
                }
                else if ((shapeType == ShapeGeneratorFactory.ShapeType.Sphere && i < 2) ||
                         (shapeType == ShapeGeneratorFactory.ShapeType.Cylinder && i < 3) ||
                         (shapeType == ShapeGeneratorFactory.ShapeType.Cone && i < 3)||
                         (shapeType == ShapeGeneratorFactory.ShapeType.Prism && i < 1))
                {
                    parameters[i] = int.Parse(inputs[i]); // 细分参数为整数
                }
                else
                {
                    parameters[i] = float.Parse(inputs[i]); // 其他参数为浮点数
                }
            }

            return parameters;
        }
    }
}