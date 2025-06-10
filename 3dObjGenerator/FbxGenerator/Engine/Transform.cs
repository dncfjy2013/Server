using OpenTK.Mathematics;

namespace FbxGenerator.Engine
{
    public class Transform : Component
    {
        public Vector3 Position { get; set; } = Vector3.Zero;
        public Quaternion Rotation { get; set; } = Quaternion.Identity;
        public Vector3 Scale { get; set; } = Vector3.One;

        public Matrix4 WorldMatrix =>
            Matrix4.CreateScale(Scale) *
            Matrix4.CreateFromQuaternion(Rotation) *
            Matrix4.CreateTranslation(Position);

        public Vector3 Forward => Rotation * Vector3.UnitZ;
        public Vector3 Up => Rotation * Vector3.UnitY;
        public Vector3 Right => Rotation * Vector3.UnitX;
    }
}