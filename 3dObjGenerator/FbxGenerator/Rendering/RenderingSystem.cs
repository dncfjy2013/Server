using FbxGenerator.Engine;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace FbxGenerator.Rendering
{
    public class RenderingSystem : SystemBase, IRenderSystem
    {
        private readonly GameWindow _window;
        private ShaderProgram? _shaderProgram;
        private Camera? _mainCamera;

        public RenderingSystem(GameWindow window)
        {
            _window = window;
        }

        public override void Initialize(Scene scene, Application application)
        {
            base.Initialize(scene, application);

            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.CullFace);
            GL.CullFace(CullFaceMode.Back);

            _shaderProgram = new ShaderProgram(
                @"Assets/Shaders/vertex_shader.glsl",
                @"Assets/Shaders/fragment_shader.glsl");

            // 查找主相机
            foreach (var gameObject in Scene.GameObjects)
            {
                var camera = gameObject.GetComponent<Camera>();
                if (camera != null && camera.IsMainCamera)
                {
                    _mainCamera = camera;
                    break;
                }
            }
        }

        public void Render(float deltaTime)
        {
            GL.ClearColor(0.1f, 0.1f, 0.1f, 1.0f);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            if (_shaderProgram == null || _mainCamera == null)
                return;

            _shaderProgram.Use();

            // 设置相机相关的uniforms
            _shaderProgram.SetMatrix4("view", _mainCamera.GetViewMatrix());
            _shaderProgram.SetMatrix4("projection", _mainCamera.GetProjectionMatrix(_window.Size.X, _window.Size.Y));

            // 渲染所有具有MeshRenderer组件的游戏对象
            foreach (var gameObject in Scene.GameObjects)
            {
                var meshRenderer = gameObject.GetComponent<MeshRenderer>();
                if (meshRenderer != null)
                {
                    meshRenderer.Render(_shaderProgram, gameObject.Transform);
                }
            }
        }
    }
}