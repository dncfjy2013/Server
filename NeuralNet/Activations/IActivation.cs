using NeuralNetworkLibrary.Core;

namespace NeuralNetworkLibrary.Activations
{
    /// <summary>
    /// 激活函数接口
    /// </summary>
    public interface IActivation
    {
        ITensor Activate(ITensor x);
        ITensor Derivative(ITensor x);
        ITensor DerivativeFromOutput(ITensor output);
    }
}
