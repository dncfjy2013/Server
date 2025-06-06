using StlGenerator.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StlGenerator.Parsers
{
    /// <summary>
    /// STL文件写入器
    /// </summary>
    public static class StlFileWriter
    {
        /// <summary>
        /// 将模型保存为STL文件
        /// </summary>
        public static void SaveModelToStl(Model model, string filePath, bool binary)
        {
            if (binary)
                SaveAsBinaryStl(model, filePath);
            else
                SaveAsAsciiStl(model, filePath);
        }

        private static void SaveAsAsciiStl(Model model, string filePath)
        {
            using (var writer = new StreamWriter(filePath))
            {
                writer.WriteLine("solid GeneratedModel");

                foreach (var triangle in model.Triangles)
                {
                    writer.WriteLine($"  facet normal {triangle.Normal.X:F6} {triangle.Normal.Y:F6} {triangle.Normal.Z:F6}");
                    writer.WriteLine("    outer loop");
                    writer.WriteLine($"      vertex {triangle.Vertex1.X:F6} {triangle.Vertex1.Y:F6} {triangle.Vertex1.Z:F6}");
                    writer.WriteLine($"      vertex {triangle.Vertex2.X:F6} {triangle.Vertex2.Y:F6} {triangle.Vertex2.Z:F6}");
                    writer.WriteLine($"      vertex {triangle.Vertex3.X:F6} {triangle.Vertex3.Y:F6} {triangle.Vertex3.Z:F6}");
                    writer.WriteLine("    endloop");
                    writer.WriteLine("  endfacet");
                }

                writer.WriteLine("endsolid GeneratedModel");
            }
        }

        private static void SaveAsBinaryStl(Model model, string filePath)
        {
            using (var writer = new BinaryWriter(File.Open(filePath, FileMode.Create)))
            {
                // 写入80字节的文件头
                for (int i = 0; i < 80; i++)
                {
                    writer.Write((byte)0);
                }

                // 写入三角形数量
                writer.Write((uint)model.Triangles.Count);

                // 写入每个三角形
                foreach (var triangle in model.Triangles)
                {
                    writer.Write(triangle.Normal.X);
                    writer.Write(triangle.Normal.Y);
                    writer.Write(triangle.Normal.Z);

                    writer.Write(triangle.Vertex1.X);
                    writer.Write(triangle.Vertex1.Y);
                    writer.Write(triangle.Vertex1.Z);

                    writer.Write(triangle.Vertex2.X);
                    writer.Write(triangle.Vertex2.Y);
                    writer.Write(triangle.Vertex2.Z);

                    writer.Write(triangle.Vertex3.X);
                    writer.Write(triangle.Vertex3.Y);
                    writer.Write(triangle.Vertex3.Z);

                    writer.Write((ushort)0); // 属性字节
                }
            }
        }
    }
}
