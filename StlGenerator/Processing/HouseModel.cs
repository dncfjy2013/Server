using OpenTK.Mathematics;
using StlGenerator.Models;
using StlGenerator.Parsers;
using StlGenerator.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace StlGenerator.Processing
{
    internal class HouseModel
    {
        // 主程序示例
        public static void CreateHouseModel()
        {
            // 创建模型合并器
            ModelMerger merger = new ModelMerger();

            // 生成房子主体（立方体）
            ShapeGeneratorFactory.CreateAndRenderShape(
                ShapeGeneratorFactory.ShapeType.Cuboid,
                3.0f,  // 长度
                2.0f,  // 宽度
                1.5f   // 高度
            );

            // 保存房子主体到文件
            string baseFilePath = "house_base.stl";
            Model baseModel = ShapeGeneratorFactory.CreateGenerator(
                ShapeGeneratorFactory.ShapeType.Cuboid,
            3.0f, 2.0f, 1.5f
            ).GenerateModel();

            StlFileWriter.SaveModelToStl(baseModel, baseFilePath, true);

            // 生成屋顶（四棱锥）
            string roofFilePath = "house_roof.stl";
            Model roofModel = ShapeGeneratorFactory.CreateGenerator(
                ShapeGeneratorFactory.ShapeType.Pyramid,
                3.0f,  // 底面长度
                2.0f,  // 底面宽度
                1.0f   // 高度
            ).GenerateModel();

            StlFileWriter.SaveModelToStl(roofModel, roofFilePath, true);

            // 添加房子主体（放置在原点）
            merger.AddModelFromFile(
                baseFilePath,
                Vector3.Zero,              // 位置
                Quaternion.Identity,       // 旋转
                Vector3.One                // 缩放
            );

            // 计算主体顶部位置
            BoundingBox baseBounds = baseModel.CalculateBoundingBox();
            float topZ = baseBounds.Max.Z;

            // 添加屋顶（放置在主体上方）
            merger.AddModelFromFile(
                roofFilePath,
                new Vector3(0, 0, topZ),   // 位置（在主体上方）
                Quaternion.Identity,       // 旋转
                Vector3.One                // 缩放
            );

            // 合并模型
            Model houseModel = merger.Merge();

            // 保存合并后的房子模型
            string houseFilePath = "complete_house.stl";
            StlFileWriter.SaveModelToStl(houseModel, houseFilePath, true);

            Console.WriteLine("房子模型已生成并保存到: " + houseFilePath);

            // 渲染最终的房子模型
            using (StlViewer viewer = new StlViewer(houseModel))
            {
                viewer.Title = "房子模型";
                viewer.Run();
            }
        }
    }
}
