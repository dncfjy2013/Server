using FbxGenerator.Engine;
using OpenTK.Mathematics;

namespace FbxGenerator.Physics
{
    public class BoxCollider : Collider
    {
        public Vector3 Size { get; set; } = Vector3.One;

        public override bool CheckCollision(Collider other, out CollisionInfo collisionInfo)
        {
            collisionInfo = new CollisionInfo();

            if (other is BoxCollider boxCollider)
            {
                return CollisionDetector.CheckBoxBoxCollision(this, boxCollider, out collisionInfo);
            }

            return false;
        }

        public override Vector3 GetCenter()
        {
            return GameObject.Transform.Position;
        }
    }
}