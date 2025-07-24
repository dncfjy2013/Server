using NeuralNetworkLibrary.Activations;
using NeuralNetworkLibrary.Core;
using NeuralNetworkLibrary.Optimizers;
using System;

namespace NeuralNetworkLibrary.Layers
{
    /// <summary>
    /// 激活层层实现
    /// </summary>
    public class ActivationLayer : BaseLayer
    {
        private IActivation _activation;
        private ITensor _input; // 缓存输入用于反向传播

        public ActivationLayer(IActivation activation, string name = "Activation") : base(name)
        {
            _activation = activation;
        }

        public override void SetInputShape(TensorShape inputShape)
        {
            InputShape = inputShape;
            OutputShape = inputShape.Clone(); // 激活函数不改变形状
        }

        public override void Initialize(Random random)
        {
            // 激活层没有参数需要初始化
        }

        public override ITensor Forward(ITensor input, bool isTraining = true)
        {
            _input = isTraining ? input.Clone() : null;
            var output = new Tensor(OutputShape.Dimensions);
            
            for (int i = 0; i < input.Size; i++)
            {
                output.Data[i] = _activation.Activate(input.Data[i]);
            }
            
            return output;
        }

        public override ITensor Backward(ITensor gradient, float learningRate)
        {
            var inputGradient = new Tensor(InputShape.Dimensions);
            
            for (int i = 0; i < gradient.Size; i++)
            {
                inputGradient.Data[i] = gradient.Data[i] * _activation.Derivative(_input.Data[i]);
            }
            
            return inputGradient;
        }

        public override void UpdateParameters(IOptimizer optimizer)
        {

        }
    }
}
