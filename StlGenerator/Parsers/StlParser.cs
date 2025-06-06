using OpenTK.Mathematics;
using StlGenerator.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StlGenerator.Parsers
{
    /// <summary>
    /// STL文件解析器
    /// </summary>
    public class StlParser
    {
        public Model ParseStlFile(string filePath)
        {
            Model model = new Model();
            try
            {
                using (FileStream fs = new FileStream(filePath, FileMode.Open))
                {
                    using (BinaryReader reader = new BinaryReader(fs))
                    {
                        byte[] header = reader.ReadBytes(80);
                        if (IsBinaryStl(header))
                            ParseBinaryStl(reader, model);
                        else
                        {
                            fs.Position = 0;
                            using (StreamReader sr = new StreamReader(fs))
                                ParseAsciiStl(sr, model);
                        }
                    }
                }
                Console.WriteLine($"成功加载STL模型，包含{model.Triangles.Count}个三角形");
                return model;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"加载STL文件失败: {ex.Message}");
                return model;
            }
        }

        private bool IsBinaryStl(byte[] header)
        {
            string headerStr = System.Text.Encoding.ASCII.GetString(header).Trim();
            return !headerStr.StartsWith("solid", StringComparison.OrdinalIgnoreCase);
        }

        private void ParseBinaryStl(BinaryReader reader, Model model)
        {
            int triangleCount = (int)reader.ReadUInt32();
            Random random = new Random();

            for (int i = 0; i < triangleCount; i++)
            {
                Vector3 normal = new Vector3(
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle()
                );

                Vector3 v1 = new Vector3(
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle()
                );

                Vector3 v2 = new Vector3(
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle()
                );

                Vector3 v3 = new Vector3(
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle()
                );

                reader.ReadUInt16(); // 跳过属性字节

                Color4 color = new Color4(
                    (normal.X + 1) / 2 * 0.8f + 0.2f,
                    (normal.Y + 1) / 2 * 0.8f + 0.2f,
                    (normal.Z + 1) / 2 * 0.8f + 0.2f,
                    1.0f
                );

                model.Triangles.Add(new Triangle(normal, v1, v2, v3, color));
            }
        }

        private void ParseAsciiStl(StreamReader reader, Model model)
        {
            string line;
            Random random = new Random();

            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();
                if (line.StartsWith("facet normal"))
                {
                    string[] parts = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    Vector3 normal = new Vector3(
                        float.Parse(parts[2]),
                        float.Parse(parts[3]),
                        float.Parse(parts[4])
                    );

                    Vector3 v1 = Vector3.Zero, v2 = Vector3.Zero, v3 = Vector3.Zero;
                    int vertexCount = 0;

                    while ((line = reader.ReadLine()) != null)
                    {
                        line = line.Trim();
                        if (line.StartsWith("vertex"))
                        {
                            string[] vertexParts = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                            Vector3 vertex = new Vector3(
                                float.Parse(vertexParts[1]),
                                float.Parse(vertexParts[2]),
                                float.Parse(vertexParts[3])
                            );

                            switch (vertexCount)
                            {
                                case 0: v1 = vertex; break;
                                case 1: v2 = vertex; break;
                                case 2: v3 = vertex; break;
                            }
                            vertexCount++;
                        }
                        else if (line.StartsWith("endfacet"))
                        {
                            Color4 color = new Color4(
                                (normal.X + 1) / 2 * 0.8f + 0.2f,
                                (normal.Y + 1) / 2 * 0.8f + 0.2f,
                                (normal.Z + 1) / 2 * 0.8f + 0.2f,
                                1.0f
                            );

                            model.Triangles.Add(new Triangle(normal, v1, v2, v3, color));
                            break;
                        }
                    }
                }
            }
        }
    }
}
