using FbxGenerator.Engine;
using OpenTK.Mathematics;

namespace FbxGenerator.Physics
{
    public abstract class Collider : Component
    {
        public abstract bool CheckCollision(Collider other, out CollisionInfo collisionInfo);

        public virtual void OnCollision(CollisionInfo collisionInfo)
        {
            // 碰撞响应逻辑
        }

        public abstract Vector3 GetCenter();
    }
}