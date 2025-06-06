using OpenTK.Mathematics;
using StlGenerator.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace StlGenerator.Core
{
    /// <summary>
    /// 形状生成器基类，提供通用功能实现
    /// </summary>
    public abstract class ShapeGenerator : IModelGenerator
    {
        protected Model _model;

        public abstract string ShapeName { get; }

        public Model GenerateModel()
        {
            _model = new Model();
            GenerateShapeGeometry();
            return _model;
        }

        public void SaveToStl(string filePath, bool binary = false)
        {
            if (_model == null)
                GenerateModel();

            // 使用STL解析器中的工具方法保存模型
            StlGenerator.Parsers.StlFileWriter.SaveModelToStl(_model, filePath, binary);
        }

        /// <summary>
        /// 由子类实现的形状几何生成方法
        /// </summary>
        protected abstract void GenerateShapeGeometry();

        /// <summary>
        /// 向模型添加三角形面
        /// </summary>
        protected void AddTriangle(Vector3 normal, Vector3 v1, Vector3 v2, Vector3 v3, Color4 color)
        {
            _model.Triangles.Add(new Models.Triangle(normal, v1, v2, v3, color));
        }

        /// <summary>
        /// 生成基于法向量的颜色
        /// </summary>
        protected Color4 GenerateColorBasedOnNormal(Vector3 normal)
        {
            return new Color4(
                (normal.X + 1) / 2 * 0.8f + 0.2f,
                (normal.Y + 1) / 2 * 0.8f + 0.2f,
                (normal.Z + 1) / 2 * 0.8f + 0.2f,
                1.0f
            );
        }
    }
}
