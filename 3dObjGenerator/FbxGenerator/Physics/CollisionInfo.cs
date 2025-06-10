using OpenTK.Mathematics;

namespace FbxGenerator.Physics
{
    public struct CollisionInfo
    {
        public Collider Other;
        public Vector3 Normal;
        public float Penetration;
        public Vector3 ContactPoint;
    }
}