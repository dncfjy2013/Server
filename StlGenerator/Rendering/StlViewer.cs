using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using StlGenerator.Models;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StlGenerator.Rendering
{
    /// <summary>
    /// STL文件查看器，基于OpenTK实现
    /// </summary>
    public class StlViewer : GameWindow
    {
        private Model _model;
        private OpenGLRenderer _renderer;
        private Matrix4 _modelMatrix;
        private Matrix4 _viewMatrix;
        private Matrix4 _projectionMatrix;
        private Vector2 _lastMousePosition;
        private bool _isRotating = false;
        private bool _isPanning = false;
        private float _zoom = 1.0f;
        private Vector3 _rotation = Vector3.Zero;
        private Vector3 _position = Vector3.Zero;
        private float _cameraDistance = 5.0f;
        private Vector3 _cameraTarget = Vector3.Zero;
        private Vector3 _upVector = Vector3.UnitY;
        private float _fieldOfView = 60.0f;
        private float _nearPlane = 0.1f;
        private float _farPlane = 1000.0f;
        private Vector3 _lightPosition = new Vector3(10.0f, 10.0f, 10.0f);

        public StlViewer(string stlFilePath)
            : base(GameWindowSettings.Default, new NativeWindowSettings()
            {
                ClientSize = new Vector2i(1280, 720),
                Title = "STL文件查看器",
                APIVersion = new Version(4, 6)
            })
        {
            Parsers.StlParser parser = new Parsers.StlParser();
            _model = parser.ParseStlFile(stlFilePath);

            if (_model.Triangles.Count > 0)
            {
                BoundingBox box = _model.CalculateBoundingBox();
                Vector3 center = (box.Min + box.Max) / 2;
                float maxDimension = Math.Max(Math.Max(box.Max.X - box.Min.X, box.Max.Y - box.Min.Y), box.Max.Z - box.Min.Z);

                _model.Position = -center; // 居中显示
                _cameraDistance = maxDimension * 2.0f;
                _fieldOfView = 45.0f;
            }
        }
        // 在StlViewer类中添加新构造函数
        public StlViewer(Model model)
            : base(GameWindowSettings.Default, new NativeWindowSettings()
            {
                ClientSize = new Vector2i(1280, 720),
                Title = "STL模型查看器",
                APIVersion = new Version(4, 6)
            })
        {
            _model = model;

            if (_model.Triangles.Count > 0)
            {
                BoundingBox box = _model.CalculateBoundingBox();
                Vector3 center = (box.Min + box.Max) / 2;
                float maxDimension = Math.Max(Math.Max(box.Max.X - box.Min.X, box.Max.Y - box.Min.Y), box.Max.Z - box.Min.Z);

                _model.Position = -center; // 居中显示
                _cameraDistance = maxDimension * 2.0f;
                _fieldOfView = 45.0f;
            }
        }
        protected override void OnLoad()
        {
            base.OnLoad();

            GL.ClearColor(0.1f, 0.1f, 0.1f, 1.0f);
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.CullFace);
            GL.CullFace(TriangleFace.Back);

            _renderer = new OpenGLRenderer();
            _renderer.Initialize(_model);

            CursorState = CursorState.Normal;
        }

        protected override void OnRenderFrame(FrameEventArgs args)
        {
            base.OnRenderFrame(args);

            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            if (_model.Triangles.Count > 0 && _renderer != null)
            {
                // 设置模型矩阵
                _modelMatrix = Matrix4.CreateScale(_model.Scale * _zoom) *
                              Matrix4.CreateRotationX(_rotation.X) *
                              Matrix4.CreateRotationY(_rotation.Y) *
                              Matrix4.CreateTranslation(_model.Position + _position);

                // 设置视图矩阵
                Vector3 cameraPosition = _cameraTarget + new Vector3(0, 0, -_cameraDistance);
                _viewMatrix = Matrix4.LookAt(cameraPosition, _cameraTarget, _upVector);

                // 设置投影矩阵
                _projectionMatrix = Matrix4.CreatePerspectiveFieldOfView(
                    MathHelper.DegreesToRadians(_fieldOfView),
                    (float)Size.X / Size.Y,
                    _nearPlane,
                    _farPlane
                );

                // 渲染模型
                _renderer.Render(_modelMatrix, _viewMatrix, _projectionMatrix, _lightPosition, cameraPosition);
            }

            SwapBuffers();
        }

        protected override void OnUpdateFrame(FrameEventArgs args)
        {
            base.OnUpdateFrame(args);

            if (!IsFocused)
                return;

            var input = KeyboardState;

            if (input.IsKeyDown(Keys.Escape))
            {
                Close();
                return;
            }

            // 处理鼠标输入
            var mouse = MouseState;

            if (mouse.ScrollDelta.Y != 0)
            {
                // 缩放
                _zoom *= (1.0f - mouse.ScrollDelta.Y * 0.1f);
                _zoom = MathHelper.Clamp(_zoom, 0.1f, 10.0f);
            }

            if (mouse.IsButtonDown(MouseButton.Right))
            {
                // 旋转
                if (!_isRotating)
                {
                    _isRotating = true;
                    _lastMousePosition = new Vector2(mouse.X, mouse.Y);
                    CursorState = CursorState.Grabbed;
                }
                else
                {
                    Vector2 delta = new Vector2(mouse.X, mouse.Y) - _lastMousePosition;
                    _rotation.Y += delta.X * 0.01f;
                    _rotation.X += delta.Y * 0.01f;
                    _lastMousePosition = new Vector2(mouse.X, mouse.Y);
                }
            }
            else if (mouse.IsButtonDown(MouseButton.Middle))
            {
                // 平移
                if (!_isPanning)
                {
                    _isPanning = true;
                    _lastMousePosition = new Vector2(mouse.X, mouse.Y);
                    CursorState = CursorState.Grabbed;
                }
                else
                {
                    Vector2 delta = new Vector2(mouse.X, mouse.Y) - _lastMousePosition;
                    float panSpeed = 0.01f * _cameraDistance;
                    _position.X -= delta.X * panSpeed;
                    _position.Y += delta.Y * panSpeed;
                    _lastMousePosition = new Vector2(mouse.X, mouse.Y);
                }
            }
            else
            {
                if (_isRotating || _isPanning)
                {
                    CursorState = CursorState.Normal;
                }
                _isRotating = false;
                _isPanning = false;
            }
        }

        protected override void OnResize(ResizeEventArgs e)
        {
            base.OnResize(e);
            GL.Viewport(0, 0, Size.X, Size.Y);
        }

        protected override void OnUnload()
        {
            base.OnUnload();

            if (_renderer != null)
                _renderer.Dispose();
        }
    }
}
