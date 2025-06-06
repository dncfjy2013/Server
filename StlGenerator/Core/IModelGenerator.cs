// Core/IModelGenerator.cs
using System;
using System.Drawing;
using System.Reflection;
using OpenTK.Mathematics;
using StlGenerator.Models;

namespace StlGenerator.Core
{
    /// <summary>
    /// 模型生成器接口，定义生成3D模型的标准方法
    /// </summary>
    public interface IModelGenerator
    {
        /// <summary>
        /// 生成3D模型数据
        /// </summary>
        Model GenerateModel();

        /// <summary>
        /// 将模型保存为STL文件
        /// </summary>
        void SaveToStl(string filePath, bool binary = false);

        /// <summary>
        /// 模型名称
        /// </summary>
        string ShapeName { get; }
    }   
}