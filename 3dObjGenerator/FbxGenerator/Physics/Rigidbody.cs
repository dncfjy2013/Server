using FbxGenerator.Engine;
using OpenTK.Mathematics;

namespace FbxGenerator.Physics
{
    public class Rigidbody : Component
    {
        public float Mass { get; set; } = 1.0f;
        public bool UseGravity { get; set; } = true;
        public bool IsKinematic { get; set; } = false;
        public Vector3 Velocity { get; set; } = Vector3.Zero;
        public Vector3 AngularVelocity { get; set; } = Vector3.Zero;

        private Vector3 _forceAccumulator = Vector3.Zero;
        private Vector3 _torqueAccumulator = Vector3.Zero;

        public void AddForce(Vector3 force)
        {
            _forceAccumulator += force;
        }

        public void AddTorque(Vector3 torque)
        {
            _torqueAccumulator += torque;
        }

        public override void Update(float deltaTime)
        {
            if (IsKinematic)
                return;

            // 计算加速度
            var acceleration = _forceAccumulator / Mass;

            // 更新速度
            Velocity += acceleration * deltaTime;

            // 更新位置
            GameObject.Transform.Position += Velocity * deltaTime;

            // 重置累加器
            _forceAccumulator = Vector3.Zero;
            _torqueAccumulator = Vector3.Zero;
        }
    }
}