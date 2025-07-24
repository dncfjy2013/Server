using NeuralNetworkLibrary.Core;
using NeuralNetworkLibrary.Optimizers;
using System.Text.Json.Nodes;

namespace NeuralNetworkLibrary.Layers
{
    /// <summary>
    /// 所有网络层的基类
    /// </summary>
    public abstract class BaseLayer : ILayer
    {
        public string Name { get; }
        public TensorShape InputShape { get; protected set; }
        public TensorShape OutputShape { get; protected set; }
        public virtual bool HasParameters => false;

        protected BaseLayer(string name)
        {
            Name = name;
        }

        public abstract void Initialize(Random random);
        public abstract void SetInputShape(TensorShape inputShape);
        public abstract ITensor Forward(ITensor input, bool isTraining = true);
        public abstract ITensor Backward(ITensor gradient, float learningRate);
        public void UpdateParameters(IOptimizer optimizer)
        {

        }
        public bool LoadParameters(JsonArray param)
        {
            return false;
        }
        public bool GetParameters(JsonArray param)
        {
            return false;
        }
    }
}
