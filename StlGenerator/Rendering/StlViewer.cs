using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using StlGenerator.Models;
using System;
using System.Drawing;
using System.Linq;

namespace StlGenerator.Rendering
{
    /// <summary>
    /// STL文件查看器，基于OpenTK实现
    /// </summary>
    public class StlViewer : GameWindow
    {
        private Model _model;                 // 存储加载的3D模型数据
        private OpenGLRenderer _renderer;     // OpenGL渲染器，负责模型的渲染
        private Matrix4 _modelMatrix;         // 模型变换矩阵（缩放、旋转、平移）
        private Matrix4 _viewMatrix;          // 视图矩阵（相机位置和方向）
        private Matrix4 _projectionMatrix;    // 投影矩阵（透视效果）
        private Vector2 _lastMousePosition;   // 上次鼠标位置，用于计算鼠标移动增量
        private bool _isRotating = false;     // 是否正在旋转模型
        private bool _isPanning = false;      // 是否正在平移模型
        private float _zoom = 1.0f;           // 缩放因子
        private Vector3 _rotation = Vector3.Zero; // 模型旋转角度
        private Vector3 _position = Vector3.Zero; // 模型平移位置
        private float _cameraDistance = 5.0f; // 相机与目标点的距离
        private Vector3 _cameraTarget = Vector3.Zero; // 相机瞄准的目标点
        private Vector3 _upVector = Vector3.UnitY;    // 相机的上方向
        private float _fieldOfView = 60.0f;   // 相机视角（垂直方向角度）
        private float _nearPlane = 0.1f;      // 近裁剪面距离
        private float _farPlane = 1000.0f;    // 远裁剪面距离
        private Vector3 _lightPosition = new Vector3(10.0f, 10.0f, 10.0f); // 光源位置

        /// <summary>
        /// 从STL文件路径创建查看器
        /// </summary>
        /// <param name="stlFilePath">STL文件路径</param>
        public StlViewer(string stlFilePath)
            : base(GameWindowSettings.Default, new NativeWindowSettings()
            {
                ClientSize = new Vector2i(1280, 720),
                Title = "STL文件查看器",
                APIVersion = new Version(4, 6)
            })
        {
            // 解析STL文件并加载模型
            Parsers.StlParser parser = new Parsers.StlParser();
            _model = parser.ParseStlFile(stlFilePath);

            // 如果模型加载成功，计算包围盒并调整相机参数以适应模型大小
            if (_model.Triangles.Count > 0)
            {
                BoundingBox box = _model.CalculateBoundingBox();
                Vector3 center = (box.Min + box.Max) / 2;
                float maxDimension = Math.Max(Math.Max(box.Max.X - box.Min.X, box.Max.Y - box.Min.Y), box.Max.Z - box.Min.Z);

                _model.Position = -center; // 将模型平移到原点，使其居中显示
                _cameraDistance = maxDimension * 2.0f; // 设置相机距离，确保能完整看到模型
                _fieldOfView = 45.0f; // 设置合适的视角
            }
        }

        /// <summary>
        /// 从已加载的模型对象创建查看器
        /// </summary>
        /// <param name="model">模型对象</param>
        public StlViewer(Model model)
            : base(GameWindowSettings.Default, new NativeWindowSettings()
            {
                ClientSize = new Vector2i(1280, 720),
                Title = "STL模型查看器",
                APIVersion = new Version(4, 6)
            })
        {
            _model = model;

            // 如果模型有三角形数据，计算包围盒并调整相机参数
            if (_model.Triangles.Count > 0)
            {
                BoundingBox box = _model.CalculateBoundingBox();
                Vector3 center = (box.Min + box.Max) / 2;
                float maxDimension = Math.Max(Math.Max(box.Max.X - box.Min.X, box.Max.Y - box.Min.Y), box.Max.Z - box.Min.Z);

                _model.Position = -center; // 居中显示模型
                _cameraDistance = maxDimension * 2.0f; // 设置合适的相机距离
                _fieldOfView = 45.0f; // 设置视角
            }
        }

        /// <summary>
        /// 窗口加载时调用，初始化OpenGL设置和渲染器
        /// </summary>
        protected override void OnLoad()
        {
            base.OnLoad();

            // 设置清屏颜色和启用深度测试、背面剔除
            GL.ClearColor(0.1f, 0.1f, 0.1f, 1.0f);
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.CullFace);
            GL.CullFace(TriangleFace.Back);

            // 初始化渲染器并传入模型数据
            _renderer = new OpenGLRenderer();
            _renderer.Initialize(_model);

            // 设置鼠标光标状态
            CursorState = CursorState.Normal;
        }

        /// <summary>
        /// 渲染帧时调用，负责绘制场景
        /// </summary>
        /// <param name="args">帧事件参数</param>
        protected override void OnRenderFrame(FrameEventArgs args)
        {
            base.OnRenderFrame(args);

            // 清除颜色缓冲区和深度缓冲区
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            // 如果模型有数据且渲染器已初始化，则渲染模型
            if (_model.Triangles.Count > 0 && _renderer != null)
            {
                // 构建模型矩阵：先缩放，再旋转，最后平移
                _modelMatrix = Matrix4.CreateScale(_model.Scale * _zoom) *
                              Matrix4.CreateRotationX(_rotation.X) *
                              Matrix4.CreateRotationY(_rotation.Y) *
                              Matrix4.CreateTranslation(_model.Position + _position);

                // 构建视图矩阵：设置相机位置、目标点和上方向
                Vector3 cameraPosition = _cameraTarget + new Vector3(0, 0, -_cameraDistance);
                _viewMatrix = Matrix4.LookAt(cameraPosition, _cameraTarget, _upVector);

                // 构建投影矩阵：设置透视投影参数
                _projectionMatrix = Matrix4.CreatePerspectiveFieldOfView(
                    MathHelper.DegreesToRadians(_fieldOfView),
                    (float)Size.X / Size.Y,  // 宽高比
                    _nearPlane,
                    _farPlane
                );

                // 调用渲染器渲染模型，传入各种变换矩阵和光源信息
                _renderer.Render(_modelMatrix, _viewMatrix, _projectionMatrix, _lightPosition, cameraPosition);
            }

            // 交换前后缓冲区，显示渲染结果
            SwapBuffers();
        }

        /// <summary>
        /// 更新帧时调用，处理用户输入和游戏状态更新
        /// </summary>
        /// <param name="args">帧事件参数</param>
        protected override void OnUpdateFrame(FrameEventArgs args)
        {
            base.OnUpdateFrame(args);

            // 如果窗口没有焦点，不处理输入
            if (!IsFocused)
                return;

            // 处理键盘输入
            var input = KeyboardState;

            // 按ESC键退出程序
            if (input.IsKeyDown(Keys.Escape))
            {
                Close();
                return;
            }

            // 处理鼠标输入
            var mouse = MouseState;

            // 鼠标滚轮控制缩放
            if (mouse.ScrollDelta.Y != 0)
            {
                _zoom *= (1.0f - mouse.ScrollDelta.Y * 0.1f);
                _zoom = MathHelper.Clamp(_zoom, 0.1f, 10.0f); // 限制缩放范围
            }

            // 右键拖动控制旋转
            if (mouse.IsButtonDown(MouseButton.Right))
            {
                if (!_isRotating)
                {
                    _isRotating = true;
                    _lastMousePosition = new Vector2(mouse.X, mouse.Y);
                    CursorState = CursorState.Grabbed; // 隐藏光标并锁定
                }
                else
                {
                    // 计算鼠标移动增量并更新旋转角度
                    Vector2 delta = new Vector2(mouse.X, mouse.Y) - _lastMousePosition;
                    _rotation.Y += delta.X * 0.01f; // 水平移动控制Y轴旋转
                    _rotation.X += delta.Y * 0.01f; // 垂直移动控制X轴旋转
                    _lastMousePosition = new Vector2(mouse.X, mouse.Y);
                }
            }
            // 中键拖动控制平移
            else if (mouse.IsButtonDown(MouseButton.Middle))
            {
                if (!_isPanning)
                {
                    _isPanning = true;
                    _lastMousePosition = new Vector2(mouse.X, mouse.Y);
                    CursorState = CursorState.Grabbed; // 隐藏光标并锁定
                }
                else
                {
                    // 计算鼠标移动增量并更新平移位置
                    Vector2 delta = new Vector2(mouse.X, mouse.Y) - _lastMousePosition;
                    float panSpeed = 0.01f * _cameraDistance; // 平移速度与相机距离成正比
                    _position.X -= delta.X * panSpeed; // 水平移动控制X轴平移
                    _position.Y += delta.Y * panSpeed; // 垂直移动控制Y轴平移
                    _lastMousePosition = new Vector2(mouse.X, mouse.Y);
                }
            }
            // 释放鼠标按键后恢复光标状态
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

        /// <summary>
        /// 窗口大小改变时调用，调整视口
        /// </summary>
        /// <param name="e">调整大小事件参数</param>
        protected override void OnResize(ResizeEventArgs e)
        {
            base.OnResize(e);
            GL.Viewport(0, 0, Size.X, Size.Y); // 设置OpenGL视口与窗口大小一致
        }

        /// <summary>
        /// 窗口卸载时调用，清理资源
        /// </summary>
        protected override void OnUnload()
        {
            base.OnUnload();

            // 释放渲染器资源
            if (_renderer != null)
                _renderer.Dispose();
        }
    }
}