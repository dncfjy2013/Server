using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StlGenerator.Models
{
    /// <summary>
    /// 3D模型数据结构
    /// </summary>
    public class Model
    {
        public List<Triangle> Triangles { get; set; } = new List<Triangle>();
        public Vector3 Position { get; set; } = Vector3.Zero;
        public Vector3 Rotation { get; set; } = Vector3.Zero;
        public Vector3 Scale { get; set; } = new Vector3(1, 1, 1);

        /// <summary>
        /// 计算模型边界用于相机自动定位
        /// </summary>
        public BoundingBox CalculateBoundingBox()
        {
            if (Triangles.Count == 0)
                return new BoundingBox(Vector3.Zero, Vector3.Zero);

            Vector3 min = Triangles[0].Vertex1;
            Vector3 max = Triangles[0].Vertex1;

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
