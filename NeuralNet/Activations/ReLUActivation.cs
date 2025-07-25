using NeuralNetworkLibrary.Core;

namespace NeuralNetworkLibrary.Activations
{
    /// <summary>
    /// ReLU激活函数实现
    /// </summary>
    public class ReLUActivation : IActivation
    {
        public ITensor Activate(ITensor input)
        {
            ITensor tensor = input.Clone();
            // ReLU函数公式: f(tensor) = max(0, tensor)
            for (int i = 0; i < tensor.Size; i++)
            {
                tensor.Data[i] = tensor.Data[i] > 0 ? tensor.Data[i] : 0;
            }
            return tensor;
        }

        public ITensor Derivative(ITensor input)
        {
            ITensor tensor = input.Clone();
            // ReLU导数公式: f'(tensor) = 1 if tensor > 0 else 0
            for (int i = 0; i < tensor.Size; i++)
            {
                tensor.Data[i] = tensor.Data[i] > 0 ? 1 : 0;
            }
            return tensor;
        }

        public ITensor DerivativeFromOutput(ITensor input)
        {
            ITensor tensor = input.Clone();
            // ReLU导数公式: f'(tensor) = 1 if tensor > 0 else 0
            for (int i = 0; i < tensor.Size; i++)
            {
                tensor.Data[i] = tensor.Data[i] > 0 ? 1 : 0;
            }
            return tensor;
        }
    }
}
