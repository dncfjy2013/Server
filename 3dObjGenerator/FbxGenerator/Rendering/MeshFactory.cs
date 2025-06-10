using OpenTK.Mathematics;
using System.Collections.Generic;

namespace FbxGenerator.Rendering
{
    public static class MeshFactory
    {
        public static Mesh CreateCube()
        {
            var vertices = new List<Vertex>
            {
                // 前面
                new Vertex { Position = new Vector3(-0.5f, -0.5f,  0.5f), Normal = new Vector3(0.0f, 0.0f, 1.0f), TexCoord = new Vector2(0.0f, 0.0f), Tangent = new Vector3(1.0f, 0.0f, 0.0f) },
                new Vertex { Position = new Vector3( 0.5f, -0.5f,  0.5f), Normal = new Vector3(0.0f, 0.0f, 1.0f), TexCoord = new Vector2(1.0f, 0.0f), Tangent = new Vector3(1.0f, 0.0f, 0.0f) },
                new Vertex { Position = new Vector3( 0.5f,  0.5f,  0.5f), Normal = new Vector3(0.0f, 0.0f, 1.0f), TexCoord = new Vector2(1.0f, 1.0f), Tangent = new Vector3(1.0f, 0.0f, 0.0f) },
                new Vertex { Position = new Vector3(-0.5f,  0.5f,  0.5f), Normal = new Vector3(0.0f, 0.0f, 1.0f), TexCoord = new Vector2(0.0f, 1.0f), Tangent = new Vector3(1.0f, 0.0f, 0.0f) },
                
                // 后面
                new Vertex { Position = new Vector3(-0.5f, -0.5f, -0.5f), Normal = new Vector3(0.0f, 0.0f, -1.0f), TexCoord = new Vector2(1.0f, 0.0f), Tangent = new Vector3(-1.0f, 0.0f, 0.0f) },
                new Vertex { Position = new Vector3( 0.5f, -0.5f, -0.5f), Normal = new Vector3(0.0f, 0.0f, -1.0f), TexCoord = new Vector2(0.0f, 0.0f), Tangent = new Vector3(-1.0f, 0.0f, 0.0f) },
                new Vertex { Position = new Vector3( 0.5f,  0.5f, -0.5f), Normal = new Vector3(0.0f, 0.0f, -1.0f), TexCoord = new Vector2(0.0f, 1.0f), Tangent = new Vector3(-1.0f, 0.0f, 0.0f) },
                new Vertex { Position = new Vector3(-0.5f,  0.5f, -0.5f), Normal = new Vector3(0.0f, 0.0f, -1.0f), TexCoord = new Vector2(1.0f, 1.0f), Tangent = new Vector3(-1.0f, 0.0f, 0.0f) },
                
                // 上面
                new Vertex { Position = new Vector3(-0.5f,  0.5f, -0.5f), Normal = new Vector3(0.0f, 1.0f, 0.0f), TexCoord = new Vector2(0.0f, 1.0f), Tangent = new Vector3(1.0f, 0.0f, 0.0f) },
                new Vertex { Position = new Vector3( 0.5f,  0.5f, -0.5f), Normal = new Vector3(0.0f, 1.0f, 0.0f), TexCoord = new Vector2(1.0f, 1.0f), Tangent = new Vector3(1.0f, 0.0f, 0.0f) },
                new Vertex { Position = new Vector3( 0.5f,  0.5f,  0.5f), Normal = new Vector3(0.0f, 1.0f, 0.0f), TexCoord = new Vector2(1.0f, 0.0f), Tangent = new Vector3(1.0f, 0.0f, 0.0f) },
                new Vertex { Position = new Vector3(-0.5f,  0.5f,  0.5f), Normal = new Vector3(0.0f, 1.0f, 0.0f), TexCoord = new Vector2(0.0f, 0.0f), Tangent = new Vector3(1.0f, 0.0f, 0.0f) },
                
                // 下面
                new Vertex { Position = new Vector3(-0.5f, -0.5f, -0.5f), Normal = new Vector3(0.0f, -1.0f, 0.0f), TexCoord = new Vector2(0.0f, 0.0f), Tangent = new Vector3(1.0f, 0.0f, 0.0f) },
                new Vertex { Position = new Vector3( 0.5f, -0.5f, -0.5f), Normal = new Vector3(0.0f, -1.0f, 0.0f), TexCoord = new Vector2(1.0f, 0.0f), Tangent = new Vector3(1.0f, 0.0f, 0.0f) },
                new Vertex { Position = new Vector3( 0.5f, -0.5f,  0.5f), Normal = new Vector3(0.0f, -1.0f, 0.0f), TexCoord = new Vector2(1.0f, 1.0f), Tangent = new Vector3(1.0f, 0.0f, 0.0f) },
                new Vertex { Position = new Vector3(-0.5f, -0.5f,  0.5f), Normal = new Vector3(0.0f, -1.0f, 0.0f), TexCoord = new Vector2(0.0f, 1.0f), Tangent = new Vector3(1.0f, 0.0f, 0.0f) },
                
                // 右面
                new Vertex { Position = new Vector3( 0.5f, -0.5f, -0.5f), Normal = new Vector3(1.0f, 0.0f, 0.0f), TexCoord = new Vector2(1.0f, 0.0f), Tangent = new Vector3(0.0f, 0.0f, 1.0f) },
                new Vertex { Position = new Vector3( 0.5f,  0.5f, -0.5f), Normal = new Vector3(1.0f, 0.0f, 0.0f), TexCoord = new Vector2(1.0f, 1.0f), Tangent = new Vector3(0.0f, 0.0f, 1.0f) },
                new Vertex { Position = new Vector3( 0.5f,  0.5f,  0.5f), Normal = new Vector3(1.0f, 0.0f, 0.0f), TexCoord = new Vector2(0.0f, 1.0f), Tangent = new Vector3(0.0f, 0.0f, 1.0f) },
                new Vertex { Position = new Vector3( 0.5f, -0.5f,  0.5f), Normal = new Vector3(1.0f, 0.0f, 0.0f), TexCoord = new Vector2(0.0f, 0.0f), Tangent = new Vector3(0.0f, 0.0f, 1.0f) },
                
                // 左面
                new Vertex { Position = new Vector3(-0.5f, -0.5f, -0.5f), Normal = new Vector3(-1.0f, 0.0f, 0.0f), TexCoord = new Vector2(0.0f, 0.0f), Tangent = new Vector3(0.0f, 0.0f, -1.0f) },
                new Vertex { Position = new Vector3(-0.5f,  0.5f, -0.5f), Normal = new Vector3(-1.0f, 0.0f, 0.0f), TexCoord = new Vector2(0.0f, 1.0f), Tangent = new Vector3(0.0f, 0.0f, -1.0f) },
                new Vertex { Position = new Vector3(-0.5f,  0.5f,  0.5f), Normal = new Vector3(-1.0f, 0.0f, 0.0f), TexCoord = new Vector2(1.0f, 1.0f), Tangent = new Vector3(0.0f, 0.0f, -1.0f) },
                new Vertex { Position = new Vector3(-0.5f, -0.5f,  0.5f), Normal = new Vector3(-1.0f, 0.0f, 0.0f), TexCoord = new Vector2(1.0f, 0.0f), Tangent = new Vector3(0.0f, 0.0f, -1.0f) }
            };

            var indices = new List<uint>
            {
                0, 1, 2, 2, 3, 0, // 前面
                4, 5, 6, 6, 7, 4, // 后面
                8, 9, 10, 10, 11, 8, // 上面
                12, 13, 14, 14, 15, 12, // 下面
                16, 17, 18, 18, 19, 16, // 右面
                20, 21, 22, 22, 23, 20 // 左面
            };

            return new Mesh(vertices, indices);
        }
    }
}