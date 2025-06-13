using FbxGenerator.Dll.ImGuiNETGLFW;
using FbxGenerator.Engine;
using ImGuiNET;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace FbxGenerator.UI
{
    public class UISystem : SystemBase, IDisposable
    {
        private readonly GameWindow _window;
        private bool _isInitialized = false;
        private float _deltaTime;
        private bool _disposed = false;

        public UISystem(GameWindow window)
        {
            _window = window;
        }

        public override unsafe void Initialize(Scene scene, Application application)
        {
            base.Initialize(scene, application);

            // 创建ImGui上下文
            ImGui.CreateContext();
            var io = ImGui.GetIO();

            // 配置ImGui
            io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
            io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
            io.ConfigFlags |= ImGuiConfigFlags.ViewportsEnable;

            // 设置错误回调
            ImGui_ImplGlfw.GLFW.ErrorCallback errorCallback = OnGlfwError;
            ImGui_ImplGlfw.GLFW.glfwSetErrorCallback(errorCallback);

            ImGui_ImplGlfw.GLFW.WindowPtr _WindowPtr = new ImGui_ImplGlfw.GLFW.WindowPtr
            {
                Handle = (nint)_window.WindowPtr // 指定属性名称为 Handle
            };

            // 初始化后端
            ImGui_ImplGlfw.Init(_WindowPtr, true);
            ImGui_ImplOpenGL3.Init("#version 460 core");

            // 设置样式
            SetImGuiStyle();

            _isInitialized = true;

            // 注册窗口事件
            _window.Closing += OnWindowClosing;
        }
        // 静态错误回调实现
        private static void OnGlfwError(int error, IntPtr description)
        {
            // 将 IntPtr 转换为字符串
            string errorMessage = Marshal.PtrToStringUTF8(description);

            // 输出错误信息
            Console.WriteLine($"GLFW Error ({error}): {errorMessage}");
        }

        private void SetImGuiStyle()
        {
            // 设置ImGui样式（可选）
            ImGui.StyleColorsDark();
            var style = ImGui.GetStyle();
            style.WindowRounding = 4.0f;
            style.FrameRounding = 2.0f;
            style.ScrollbarRounding = 2.0f;
        }

        private void OnWindowClosing(System.ComponentModel.CancelEventArgs e)
        {
            Dispose();
        }
        private bool _showDebugWindow = false;
        private bool _f1WasPressed = false;
        public override void Update(float deltaTime)
        {
            if (!_isInitialized)
                return;

            _deltaTime = deltaTime;

            // 处理GLFW事件
            ImGui_ImplGlfw.ProcessEvents();

            // 开始ImGui新帧
            ImGui_ImplOpenGL3.NewFrame();
            ImGui_ImplGlfw.NewFrame();
            ImGui.NewFrame();

            // 绘制UI
            DrawUI();

            // 检测F1按键状态变化
            var keyboardState = _window.KeyboardState;
            if (keyboardState.IsKeyDown(Keys.F1) && !_f1WasPressed)
            {
                _showDebugWindow = !_showDebugWindow;
            }
            _f1WasPressed = keyboardState.IsKeyDown(Keys.F1);
        }

        public void Render()
        {
            if (!_isInitialized)
                return;

            // 渲染ImGui
            ImGui.Render();
            ImGui_ImplOpenGL3.RenderDrawData(ImGui.GetDrawData());

            // 处理多视口（如果启用）
            if (ImGui.GetIO().ConfigFlags.HasFlag(ImGuiConfigFlags.ViewportsEnable))
            {
                var backupCurrentContext = ImGui.GetCurrentContext();
                ImGui.UpdatePlatformWindows();
                ImGui.RenderPlatformWindowsDefault();
                ImGui.SetCurrentContext(backupCurrentContext);
            }
        }

        private void DrawUI()
        {
            // 主窗口
            ImGui.Begin("FbxGenerator",
                ImGuiWindowFlags.NoCollapse |
                ImGuiWindowFlags.NoDocking);

            // 显示帧率信息
            ImGui.Text($"FPS: {1.0f / _deltaTime:F2}");
            ImGui.Text($"Delta: {_deltaTime * 1000:F2} ms");

            // 显示场景信息
            ImGui.Text($"Scene: {Scene?.ToString() ?? "None"}");

            // 工具栏
            if (ImGui.Button("New")) { /* 创建新场景 */ }
            ImGui.SameLine();
            if (ImGui.Button("Load")) { /* 加载场景 */ }
            ImGui.SameLine();
            if (ImGui.Button("Save")) { /* 保存场景 */ }

            // 分割线
            ImGui.Separator();

            // 场景树
            ImGui.BeginChild("SceneTree", new Vector2(200, -1), ImGuiChildFlags.None);
            DrawSceneTree();
            ImGui.EndChild();

            ImGui.SameLine();

            // 属性面板
            ImGui.BeginChild("Properties", new Vector2(-1, -1), ImGuiChildFlags.None);
            DrawPropertiesPanel();
            ImGui.EndChild();

            ImGui.End();

            // 菜单
            DrawMenuBar();

            // 调试窗口
            DrawDebugWindow();
        }

        private void DrawMenuBar()
        {
            if (ImGui.BeginMainMenuBar())
            {
                if (ImGui.BeginMenu("File"))
                {
                    if (ImGui.MenuItem("New", "Ctrl+N")) { /* 创建新场景 */ }
                    if (ImGui.MenuItem("Open", "Ctrl+O")) { /* 打开场景 */ }
                    if (ImGui.MenuItem("Save", "Ctrl+S")) { /* 保存场景 */ }
                    if (ImGui.MenuItem("Save As...")) { /* 另存为 */ }
                    ImGui.Separator();
                    if (ImGui.MenuItem("Exit")) { /* 退出 */ }
                    ImGui.EndMenu();
                }

                if (ImGui.BeginMenu("Edit"))
                {
                    if (ImGui.MenuItem("Undo", "Ctrl+Z")) { /* 撤销 */ }
                    if (ImGui.MenuItem("Redo", "Ctrl+Y")) { /* 重做 */ }
                    ImGui.Separator();
                    if (ImGui.MenuItem("Settings")) { /* 设置 */ }
                    ImGui.EndMenu();
                }

                if (ImGui.BeginMenu("Help"))
                {
                    if (ImGui.MenuItem("Documentation")) { /* 文档 */ }
                    if (ImGui.MenuItem("About")) { /* 关于 */ }
                    ImGui.EndMenu();
                }

                ImGui.EndMainMenuBar();
            }
        }

        private void DrawSceneTree()
        {
            ImGui.Text("Scene Tree");
            ImGui.Separator();

            // 这里应该显示场景中的对象树
            // 实际应用中需要遍历场景中的对象
            ImGui.BulletText("Root");
            ImGui.Indent();
            ImGui.BulletText("Camera");
            ImGui.BulletText("Lights");
            ImGui.BulletText("Models");
            ImGui.Unindent();
        }

        private void DrawPropertiesPanel()
        {
            ImGui.Text("Properties");
            ImGui.Separator();

            // 这里应该显示选中对象的属性
            // 实际应用中需要根据选中的对象显示不同的属性
            ImGui.Text("No object selected");
        }

        private void DrawDebugWindow()
        {
            // 显示ImGui演示窗口（调试用）
            if (_showDebugWindow && ImGui.Begin("Debug", ref _showDebugWindow, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.ShowDemoWindow();
                ImGui.End();
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // 清理托管资源

                    _window.Closing -= OnWindowClosing;
                }

                // 清理非托管资源
                if (_isInitialized)
                {
                    ImGui_ImplOpenGL3.Shutdown();
                    ImGui_ImplGlfw.Shutdown();
                    ImGui.DestroyContext();
                    _isInitialized = false;
                }

                _disposed = true;
            }
        }

        ~UISystem()
        {
            Dispose(false);
        }
    }
}
