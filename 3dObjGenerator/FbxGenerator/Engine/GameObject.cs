using System.Collections.Generic;

namespace FbxGenerator.Engine
{
    public class GameObject
    {
        private readonly List<Component> _components = new();

        public string Name { get; set; }
        public Transform Transform { get; }

        public GameObject(string name = "GameObject")
        {
            Name = name;
            Transform = AddComponent<Transform>();
        }

        public T AddComponent<T>() where T : Component, new()
        {
            var component = new T
            {
                GameObject = this
            };

            _components.Add(component);
            component.Initialize();

            return component;
        }

        public T? GetComponent<T>() where T : Component
        {
            return _components.OfType<T>().FirstOrDefault();
        }

        public IEnumerable<T> GetComponents<T>() where T : Component
        {
            return _components.OfType<T>();
        }
    }
}