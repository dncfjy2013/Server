using OpenTK.Mathematics;
using StlGenerator.Models;
using StlGenerator.Parsers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenTK.Graphics.OpenGL4;

namespace StlGenerator.Processing
{
    /// <summary>
    /// 模型合并器，用于拼接多个STL模型
    /// </summary>
    public class ModelMerger
    {
        private readonly List<(Model model, Matrix4 transform)> _models = new List<(Model, Matrix4)>();

        /// <summary>
        /// 添加要合并的模型
        /// </summary>
        public void AddModel(Model model, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            // 创建组合变换矩阵
            Matrix4 scaleMatrix = Matrix4.CreateScale(scale);
            Matrix4 rotationMatrix = Matrix4.CreateFromQuaternion(rotation);
            Matrix4 translationMatrix = Matrix4.CreateTranslation(position);

            Matrix4 transform = scaleMatrix * rotationMatrix * translationMatrix;
            _models.Add((model, transform));
        }

        /// <summary>
        /// 从STL文件添加模型
        /// </summary>
        public void AddModelFromFile(string filePath, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            StlParser parser = new StlParser();
            Model model = parser.ParseStlFile(filePath);
            AddModel(model, position, rotation, scale);
        }

        /// <summary>
        /// 合并所有添加的模型
        /// </summary>
        public Model Merge()
        {
            Model mergedModel = new Model();

            foreach (var (model, transform) in _models)
            {
                ApplyTransformationAndAddToModel(model, transform, mergedModel);
            }

            return mergedModel;
        }

        private void ApplyTransformationAndAddToModel(Model sourceModel, Matrix4 transform, Model targetModel)
        {
            Console.WriteLine($"Applying transformation to model with {sourceModel.Triangles.Count} triangles");

            foreach (var triangle in sourceModel.Triangles)
            {
                // 变换顶点位置
                Vector4 v1 = new Vector4(triangle.Vertex1, 1.0f) * transform;
                Vector4 v2 = new Vector4(triangle.Vertex2, 1.0f) * transform;
                Vector4 v3 = new Vector4(triangle.Vertex3, 1.0f) * transform;

                // 提取旋转部分
                Matrix3 rotationMatrix = new Matrix3(
                    transform.Row0.X, transform.Row0.Y, transform.Row0.Z,
                    transform.Row1.X, transform.Row1.Y, transform.Row1.Z,
                    transform.Row2.X, transform.Row2.Y, transform.Row2.Z
                );

                // 法线变换
                Matrix3 normalMatrix = Matrix3.Transpose(Matrix3.Invert(rotationMatrix));
                Vector3 normal = normalMatrix * triangle.Normal;
                normal = Vector3.Normalize(normal);

                // 添加变换后的三角形
                targetModel.Triangles.Add(new Triangle(
                    normal,
                    new Vector3(v1.X, v1.Y, v1.Z),
                    new Vector3(v2.X, v2.Y, v2.Z),
                    new Vector3(v3.X, v3.Y, v3.Z),
                    triangle.Color
                ));
            }
        }
    }
}
