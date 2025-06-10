using FbxGenerator.Engine;
using System.Collections.Generic;

namespace FbxGenerator.Physics
{
    public class PhysicsSystem : SystemBase
    {
        private readonly List<Rigidbody> _rigidbodies = new();
        private readonly List<Collider> _colliders = new();

        public override void OnGameObjectAdded(GameObject gameObject)
        {
            var rigidbody = gameObject.GetComponent<Rigidbody>();
            if (rigidbody != null)
            {
                _rigidbodies.Add(rigidbody);
            }

            var collider = gameObject.GetComponent<Collider>();
            if (collider != null)
            {
                _colliders.Add(collider);
            }
        }

        public override void Update(float deltaTime)
        {
            // 应用重力
            foreach (var rigidbody in _rigidbodies)
            {
                if (rigidbody.UseGravity)
                {
                    rigidbody.AddForce(new OpenTK.Mathematics.Vector3(0, -9.81f, 0) * rigidbody.Mass);
                }

                rigidbody.Update(deltaTime);
            }

            // 检测碰撞
            for (int i = 0; i < _colliders.Count; i++)
            {
                for (int j = i + 1; j < _colliders.Count; j++)
                {
                    if (CollisionDetector.CheckCollision(_colliders[i], _colliders[j], out var collisionInfo))
                    {
                        _colliders[i].OnCollision(collisionInfo);
                        _colliders[j].OnCollision(new CollisionInfo
                        {
                            Other = _colliders[i],
                            Normal = -collisionInfo.Normal,
                            Penetration = collisionInfo.Penetration
                        });
                    }
                }
            }
        }
    }
}