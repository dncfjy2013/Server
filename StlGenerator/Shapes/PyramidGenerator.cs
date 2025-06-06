using OpenTK.Mathematics;
using StlGenerator.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StlGenerator.Shapes
{
    /// <summary>
    /// 金字塔生成器（四棱锥）
    /// </summary>
    public class PyramidGenerator : ShapeGenerator
    {
        private readonly float _baseWidth;
        private readonly float _baseLength;
        private readonly float _height;

        public PyramidGenerator(float baseWidth, float baseLength, float height)
        {
            _baseWidth = baseWidth;
            _baseLength = baseLength;
            _height = height;
        }

        public override string ShapeName => "Pyramid";

        protected override void GenerateShapeGeometry()
        {
            float halfWidth = _baseWidth / 2;
            float halfLength = _baseLength / 2;

            // 定义底面四个顶点
            Vector3 v0 = new Vector3(-halfWidth, -halfLength, 0);
            Vector3 v1 = new Vector3(halfWidth, -halfLength, 0);
            Vector3 v2 = new Vector3(halfWidth, halfLength, 0);
            Vector3 v3 = new Vector3(-halfWidth, halfLength, 0);

            // 定义顶点
            Vector3 apex = new Vector3(0, 0, _height);

            // 生成底面
            Vector3 baseNormal = new Vector3(0, 0, -1);
            Color4 baseColor = GenerateColorBasedOnNormal(baseNormal);
            AddTriangle(baseNormal, v0, v1, v2, baseColor);
            AddTriangle(baseNormal, v0, v2, v3, baseColor);

            // 生成四个侧面
            GenerateSide(v0, v1, apex);
            GenerateSide(v1, v2, apex);
            GenerateSide(v2, v3, apex);
            GenerateSide(v3, v0, apex);
        }

        private void GenerateSide(Vector3 v1, Vector3 v2, Vector3 apex)
        {
            // 计算法向量
            Vector3 normal = Vector3.Normalize(Vector3.Cross(v2 - v1, apex - v1));
            Color4 color = GenerateColorBasedOnNormal(normal);

            // 添加三角形
            AddTriangle(normal, v1, v2, apex, color);
        }
    }
}
