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

        public PyramidGenerator(float baseLength, float baseWidth, float height)
        {
            _baseWidth = baseWidth;
            _baseLength = baseLength;
            _height = height;
        }

        public override string ShapeName => "Pyramid";

        protected override void GenerateShapeGeometry()
        {
            // 定义底面四个顶点（中心在原点，Y=0）
            Vector3 v0 = new Vector3(-_baseLength / 2, 0, -_baseWidth / 2);  // 左下
            Vector3 v1 = new Vector3(_baseLength / 2, 0, -_baseWidth / 2);   // 右下
            Vector3 v2 = new Vector3(_baseLength / 2, 0, _baseWidth / 2);    // 右上
            Vector3 v3 = new Vector3(-_baseLength / 2, 0, _baseWidth / 2);   // 左上

            // 定义顶点（Y轴正方向为高度）
            Vector3 apex = new Vector3(0, _height, 0);

            // 生成底面（Y轴负方向为法线）
            Vector3 baseNormal = -Vector3.UnitY;

            Color4 baseColor = GenerateColorBasedOnNormal(baseNormal);

            AddTriangle(baseNormal, v0, v1, v2, baseColor);
            AddTriangle(baseNormal, v0, v2, v3, baseColor);

            // 生成四个侧面
            AddTriangle(Vector3.Normalize(Vector3.Cross(v1 - apex, v0 - apex)), apex, v1, v0, baseColor);
            AddTriangle(Vector3.Normalize(Vector3.Cross(v2 - apex, v1 - apex)), apex, v2, v1, baseColor);
            AddTriangle(Vector3.Normalize(Vector3.Cross(v3 - apex, v2 - apex)), apex, v3, v2, baseColor);
            AddTriangle(Vector3.Normalize(Vector3.Cross(v0 - apex, v3 - apex)), apex, v0, v3, baseColor);
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
