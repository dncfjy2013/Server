using FbxGenerator.Dll;
using FbxGenerator.Engine;
using ImGuiNET;
using OpenTK.Windowing.Desktop; // 引入 GameWindow

namespace FbxGenerator.UI
{
    public class UISystem : SystemBase
    {
        private readonly GameWindow _window; // 使用 GameWindow 替代 Window
        private bool _isInitialized = false;

        public UISystem(GameWindow window)
        {
            _window = window;
        }

        public override unsafe void Initialize(Scene scene, Application application)
        {
            base.Initialize(scene, application);

            // 初始化ImGui
            ImGui.CreateContext();
            var io = ImGui.GetIO();
            io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;

            // 修正 ImGui_ImplGlfw 初始化
            ImGui_ImplGlfw.Init((nint)_window.WindowPtr, true);
            ImGui_ImplOpenGL3.Init("#version 460");

            _isInitialized = true;
        }

        public override void Update(float deltaTime)
        {
            if (!_isInitialized)
                return;

            // 更新ImGui
            ImGui_ImplOpenGL3.NewFrame();
            ImGui_ImplGlfw.NewFrame();
            ImGui.NewFrame();

            // 绘制UI
            DrawUI();

            ImGui.Render();
        }

        private void DrawUI()
        {
            ImGui.Begin("FbxGenerator");

            ImGui.Text($"Application average {1000.0f / ImGui.GetIO().Framerate:F3} ms/frame ({ImGui.GetIO().Framerate:F1} FPS)");

            // 这里可以添加更多UI元素

            ImGui.End();
        }

        public void Render()
        {
            if (!_isInitialized)
                return;

            ImGui_ImplOpenGL3.RenderDrawData(ImGui.GetDrawData());
        }
    }
}