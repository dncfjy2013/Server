namespace FbxGenerator.Engine
{
    public abstract class Component
    {
        public GameObject GameObject { get; internal set; } = null!;

        public virtual void Initialize()
        {
        }

        public virtual void Update(float deltaTime)
        {
        }
    }
}