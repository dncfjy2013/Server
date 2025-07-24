using NeuralNetworkLibrary.Core;

namespace NeuralNetworkLibrary.LossFunctions
{
    /// <summary>
    /// 损失函数接口
    /// </summary>
    public interface ILossFunction
    {
        float Calculate(ITensor predicted, ITensor target);
        ITensor CalculateGradient(ITensor predicted, ITensor target);
    }
}
