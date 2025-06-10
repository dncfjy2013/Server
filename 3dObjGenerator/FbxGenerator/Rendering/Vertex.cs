using OpenTK.Mathematics;

namespace FbxGenerator.Rendering
{
    public struct Vertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 TexCoord;
        public Vector3 Tangent;

        public static readonly int Size =
            sizeof(float) * 3 + // Position
            sizeof(float) * 3 + // Normal
            sizeof(float) * 2 + // TexCoord
            sizeof(float) * 3;  // Tangent
    }
}