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
using static StlGenerator.ShapeGeneratorFactory;

namespace StlGenerator.Processing
{
    internal class HouseModel
    {
        // 主程序示例
        public static Model CreateHouseModel(params object[] parameters)
        {
            // 创建模型合并器
            ModelMerger merger = new ModelMerger();

            float houseLength = parameters.Length > 0 ? (float)parameters[0] : 3.0f;
            float houseWidth = parameters.Length > 1 ? (float)parameters[1] : 2.0f;
            float houseHeight = parameters.Length > 2 ? (float)parameters[2] : 1.5f;
            float roofHeight = parameters.Length > 3 ? (float)parameters[3] : 1.0f;

            // 创建房子主体（Y轴为高度方向）
            Model baseModel = CreateGenerator(
                ShapeType.Cuboid,
                houseLength,  // X方向
                houseHeight,  // Y方向（高度）
                houseWidth    // Z方向
            ).GenerateModel();

            // 创建屋顶（Y轴为高度方向）
            Model roofModel = CreateGenerator(
                ShapeType.Pyramid,
                houseLength,  // X方向
                houseWidth,   // Z方向
                roofHeight    // Y方向（高度）
            ).GenerateModel();

            merger.AddModel(baseModel, Vector3.Zero, Quaternion.Identity, Vector3.One);

            // 正确放置屋顶（Y轴方向）
            merger.AddModel(
                roofModel,
                new Vector3(0, houseHeight, 0),  // 屋顶底部与主体顶部对齐
                Quaternion.Identity,
                Vector3.One
            );

            return merger.Merge();
        }
    }
}
