using System.Collections.Generic;
using System.Linq;

namespace FbxGenerator.Engine
{
    public class Scene
    {
        private readonly List<GameObject> _gameObjects = new();
        private readonly List<SystemBase> _systems = new();

        public IReadOnlyCollection<GameObject> GameObjects => _gameObjects;
        public void Initialize(Application application)
        {
            foreach (var system in _systems)
            {
                system.Initialize(this, application);
            }
        }

        public void AddGameObject(GameObject gameObject)
        {
            _gameObjects.Add(gameObject);

            foreach (var system in _systems)
            {
                system.OnGameObjectAdded(gameObject);
            }
        }

        public void AddSystem(SystemBase system)
        {
            _systems.Add(system);
        }

        public void Update(float deltaTime)
        {
            foreach (var system in _systems)
            {
                system.Update(deltaTime);
            }
        }

        public void Render(float deltaTime)
        {
            foreach (var system in _systems.OfType<IRenderSystem>())
            {
                system.Render(deltaTime);
            }
        }
    }
}