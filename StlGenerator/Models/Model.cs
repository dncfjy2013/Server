using OpenTK.Mathematics;
using System;
using System.Collections.Generic;

namespace StlGenerator.Models
{
    /// <summary>
    /// 3D模型数据结构，存储模型的几何信息和变换参数
    /// </summary>
    public class Model
    {
        /// <summary>模型的三角形面片集合</summary>
        public List<Triangle> Triangles { get; set; } = new List<Triangle>();

        /// <summary>模型的世界坐标位置（平移量）</summary>
        public Vector3 Position { get; set; } = Vector3.Zero;

        /// <summary>模型的旋转角度（绕X、Y、Z轴的欧拉角，单位：弧度）</summary>
        public Vector3 Rotation { get; set; } = Vector3.Zero;

        /// <summary>模型的缩放因子（X、Y、Z方向的缩放比例）</summary>
        public Vector3 Scale { get; set; } = new Vector3(1, 1, 1);

        /// <summary>
        /// 计算模型的轴对齐边界框（AABB）
        /// 用于相机自动定位、碰撞检测等场景
        /// </summary>
        /// <returns>包含模型所有顶点的最小边界框</returns>
        public BoundingBox CalculateBoundingBox()
        {
            if (Triangles.Count == 0)
                return new BoundingBox(Vector3.Zero, Vector3.Zero);

            // 初始化最小/最大点为第一个顶点
            Vector3 min = Triangles[0].Vertex1;
            Vector3 max = Triangles[0].Vertex1;

            // 遍历所有三角形的顶点，更新最小/最大边界
            foreach (var triangle in Triangles)
            {
                min = Vector3.ComponentMin(min, triangle.Vertex1);
                min = Vector3.ComponentMin(min, triangle.Vertex2);
                min = Vector3.ComponentMin(min, triangle.Vertex3);
                max = Vector3.ComponentMax(max, triangle.Vertex1);
                max = Vector3.ComponentMax(max, triangle.Vertex2);
                max = Vector3.ComponentMax(max, triangle.Vertex3);
            }

            return new BoundingBox(min, max);
        }
    }
}