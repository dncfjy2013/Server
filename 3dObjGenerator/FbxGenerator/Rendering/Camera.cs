using FbxGenerator.Engine;
using OpenTK.Mathematics;

namespace FbxGenerator.Rendering
{
    public class Camera : Component
    {
        public float FieldOfView { get; set; } = 45.0f;
        public float NearPlane { get; set; } = 0.1f;
        public float FarPlane { get; set; } = 1000.0f;
        public bool IsMainCamera { get; set; } = false;

        private Transform _transform; 

        public override void Initialize()
        {
            _transform = GameObject.Transform;
        }

        public Matrix4 GetViewMatrix()
        {
            return Matrix4.LookAt(
                _transform.Position,
                _transform.Position + _transform.Forward,
                _transform.Up);
        }

        public Matrix4 GetProjectionMatrix(int width, int height)
        {
            var aspectRatio = (float)width / height;
            return Matrix4.CreatePerspectiveFieldOfView(
                MathHelper.DegreesToRadians(FieldOfView),
                aspectRatio,
                NearPlane,
                FarPlane);
        }
    }
}