using FbxGenerator.Engine;
using FbxGenerator.Rendering;
using FbxGenerator.Physics;
using FbxGenerator.UI;

namespace FbxGenerator
{
    public static class Program
    {
        public static void Main()
        {
            using var application = new Application("FbxGenerator", 1280, 720);

            var scene = new Scene();
            application.LoadScene(scene);

            var uiSystem = new UISystem(application);
            scene.AddSystem(uiSystem);

            var physicsSystem = new PhysicsSystem();
            scene.AddSystem(physicsSystem);

            var renderingSystem = new RenderingSystem(application);
            scene.AddSystem(renderingSystem);

            application.Run();
        }
    }
}