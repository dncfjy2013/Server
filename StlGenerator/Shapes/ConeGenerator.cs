using OpenTK.Mathematics;
using StlGenerator.Core;
using System;
using System.Collections.Generic;

namespace StlGenerator.Shapes
{
    /// <summary>
    /// 圆锥体生成器（Y轴向上，右手坐标系）
    /// </summary>
    public class ConeGenerator : ShapeGenerator
    {
        private readonly float _radius;       // 底面半径
        private readonly float _height;       // Y轴方向高度
        private readonly int _sides;          // 侧面分段数

        public ConeGenerator(float radius, float height, int sides = 32)
        {
            if (sides < 3)
                throw new ArgumentException("侧面分段数至少为3", nameof(sides));

            _radius = radius;
            _height = height;
            _sides = sides;
        }

        public override string ShapeName => "Cone";

        protected override void GenerateShapeGeometry()
        {
            // 定义圆锥体顶点（Y轴向上）
            Vector3 apex = new Vector3(0, _height, 0);         // 顶点在Y轴正方向
            Vector3 baseCenter = new Vector3(0, 0, 0);         // 底面中心在原点

            // 生成底面（Y轴负方向为法线）
            Vector3 baseNormal = -Vector3.UnitY;
            Color4 baseColor = GenerateColorBasedOnNormal(baseNormal);

            // 生成底面（多边形近似圆形）
            for (int i = 0; i < _sides; i++)
            {
                float angle1 = i * 2 * MathHelper.Pi / _sides;
                float angle2 = (i + 1) * 2 * MathHelper.Pi / _sides;

                // 计算底面顶点（XZ平面上的圆）
                Vector3 v1 = new Vector3(
                    _radius * (float)Math.Cos(angle1),  // X坐标
                    0,                                 // Y坐标（底面在Y=0）
                    _radius * (float)Math.Sin(angle1)   // Z坐标
                );

                Vector3 v2 = new Vector3(
                    _radius * (float)Math.Cos(angle2),
                    0,
                    _radius * (float)Math.Sin(angle2)
                );

                // 添加底面三角形（中心-顶点1-顶点2）
                AddTriangle(baseNormal, baseCenter, v2, v1, baseColor);
            }

            // 生成侧面
            for (int i = 0; i < _sides; i++)
            {
                float angle1 = i * 2 * MathHelper.Pi / _sides;
                float angle2 = (i + 1) * 2 * MathHelper.Pi / _sides;

                // 计算底面顶点
                Vector3 v1 = new Vector3(
                    _radius * (float)Math.Cos(angle1),
                    0,
                    _radius * (float)Math.Sin(angle1)
                );

                Vector3 v2 = new Vector3(
                    _radius * (float)Math.Cos(angle2),
                    0,
                    _radius * (float)Math.Sin(angle2)
                );

                // 计算侧面法向量（右手定则）
                Vector3 sideNormal = Vector3.Normalize(
                    Vector3.Cross(v2 - apex, v1 - apex)  // 注意向量顺序，确保法向量朝外
                );

                Color4 sideColor = GenerateColorBasedOnNormal(sideNormal);

                // 添加侧面三角形（顶点-顶点1-顶点2）
                AddTriangle(sideNormal, apex, v1, v2, sideColor);
            }
        }
    }
}