using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using static System.Formats.Asn1.AsnWriter;

namespace FbxGenerator.Engine
{
    public class Application : GameWindow
    {
        private Scene? _currentScene;

        public Application(string title, int width, int height)
            : base(GameWindowSettings.Default, new NativeWindowSettings
            {
                Size = (width, height),
                Title = title,
                API = ContextAPI.OpenGL,
                APIVersion = new Version(4, 6),
                Profile = ContextProfile.Core
            })
        {
        }

        public void LoadScene(Scene scene)
        {
            _currentScene = scene;
            _currentScene.Initialize(this);
        }

        protected override void OnUpdateFrame(FrameEventArgs args)
        {
            base.OnUpdateFrame(args);

            if (KeyboardState.IsKeyDown(Keys.Escape))
            {
                Close();
            }

            _currentScene?.Update((float)args.Time);
        }

        protected override void OnRenderFrame(FrameEventArgs args)
        {
            base.OnRenderFrame(args);

            _currentScene?.Render((float)args.Time);

            SwapBuffers();
        }
    }
}