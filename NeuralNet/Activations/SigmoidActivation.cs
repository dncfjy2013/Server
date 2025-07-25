using NeuralNetworkLibrary.Core;

namespace NeuralNetworkLibrary.Activations
{
    /// <summary>
    /// Sigmoid激活函数实现
    /// </summary>
    public class SigmoidActivation : IActivation
    {
        public ITensor Activate(ITensor input)
        {
            ITensor tensor = input.Clone();
            // Sigmoid函数公式: σ(tensor) = 1 / (1 + exp(-tensor))
            // 这里假设ITensor可以直接转换为float
            for (int i = 0; i < tensor.Size; i++)
            {
                tensor.Data[i] = 1.0f / (1.0f + (float)System.Math.Exp(-tensor.Data[i]));
            }
            return tensor;
        }

        public ITensor Derivative(ITensor input)
        {
            ITensor tensor = input.Clone();
            // Sigmoid导数公式: σ'(x) = σ(x) * (1 - σ(x))
            ITensor sigmoid = Activate(tensor);
            for (int i = 0; i < sigmoid.Size; i++)
            {
                sigmoid.Data[i] = sigmoid.Data[i] * (1.0f - sigmoid.Data[i]);
            }
            return sigmoid;
        }

        public ITensor DerivativeFromOutput(ITensor input)
        {
            ITensor tensor = input.Clone();
            for (int i = 0; i < tensor.Size; i++)
            {
                tensor.Data[i] = tensor.Data[i] * (1.0f - tensor.Data[i]);
            }
            return tensor;
        }
    }
}
