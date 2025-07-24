using NeuralNetworkLibrary.Core;
using NeuralNetworkLibrary.Optimizers;
using System.Text.Json.Nodes;

namespace NeuralNetworkLibrary.Layers
{
    /// <summary>
    /// 所有神经网络层的接口
    /// </summary>
    public interface ILayer
    {
        string Name { get; }
        TensorShape InputShape { get; }
        TensorShape OutputShape { get; }
        bool HasParameters { get; }
        
        void Initialize(Random random);
        void SetInputShape(TensorShape inputShape);
        ITensor Forward(ITensor input, bool isTraining = true);
        ITensor Backward(ITensor gradient, float learningRate);

        void UpdateParameters(IOptimizer optimizer);

        bool LoadParameters(JsonArray param);
        bool GetParameters(JsonArray param);
    }
}
