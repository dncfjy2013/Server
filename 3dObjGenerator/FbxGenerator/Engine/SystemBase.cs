namespace FbxGenerator.Engine
{
    public abstract class SystemBase
    {
        protected Scene Scene { get; private set; } = null!;
        protected Application Application { get; private set; } = null!;

        public virtual void Initialize(Scene scene, Application application)
        {
            Scene = scene;
            Application = application;
        }

        public virtual void Update(float deltaTime)
        {
        }

        public virtual void OnGameObjectAdded(GameObject gameObject)
        {
        }
    }
}