using FbxGenerator.Engine;
using OpenTK.Mathematics;

namespace FbxGenerator.Physics
{
    public class SphereCollider : Collider
    {
        public float Radius { get; set; } = 1.0f;

        public override bool CheckCollision(Collider other, out CollisionInfo collisionInfo)
        {
            collisionInfo = new CollisionInfo();

            if (other is SphereCollider sphereCollider)
            {
                return CollisionDetector.CheckSphereSphereCollision(this, sphereCollider, out collisionInfo);
            }

            if (other is BoxCollider boxCollider)
            {
                return CollisionDetector.CheckSphereBoxCollision(this, boxCollider, out collisionInfo);
            }

            return false;
        }

        public override Vector3 GetCenter()
        {
            return GameObject.Transform.Position;
        }
    }
}