using NeuralNetworkLibrary.Core;

namespace NeuralNetworkLibrary.Activations
{
    /// <summary>
    /// Leaky ReLU激活函数实现
    /// </summary>
    public class LeakyReLUActivation : IActivation
    {
        private float _alpha;

        public LeakyReLUActivation(float alpha = 0.01f)
        {
            _alpha = alpha;
        }

        public ITensor Activate(ITensor input)
        {
            ITensor tensor = input.Clone();
            // Leaky ReLU公式: f(tensor) = tensor if tensor > 0 else alpha * tensor
            for (int i=0; i < tensor.Size; i++)
            {
                tensor.Data[i] = tensor.Data[i] > 0 ? tensor.Data[i] : _alpha * tensor.Data[i];
            }
            return tensor;
        }

        public ITensor Derivative(ITensor input)
        {
            ITensor tensor = input.Clone();
            // Leaky ReLU导数: 1 if tensor > 0 else alpha
            for (int i = 0; i < tensor.Size; i++)
            {
                tensor.Data[i] = tensor.Data[i] > 0 ? 1 : _alpha;
            }
            return tensor;
        }

        public ITensor DerivativeFromOutput(ITensor input)
        {
            ITensor tensor = input.Clone();
            for (int i = 0; i < tensor.Size; i++)
            {
                tensor.Data[i] = tensor.Data[i] > 0 ? 1 : _alpha;
            }
            return tensor;
        }
    }
}
