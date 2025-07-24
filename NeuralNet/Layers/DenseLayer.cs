using NeuralNetworkLibrary.Core;
using NeuralNetworkLibrary.Optimizers;
using System;

namespace NeuralNetworkLibrary.Layers
{
    /// <summary>
    /// 全连接层（密集层）实现
    /// </summary>
    public class DenseLayer : BaseLayer
    {
        private int _units;
        private ITensor _weights;  // [units, inputSize]
        private ITensor _biases;   // [units]
        private ITensor _input;    // 缓存输入用于反向传播
        private int _inputSize;    // 输入特征数量
        
        public override bool HasParameters => true;

        public DenseLayer(int units, string name = "Dense") : base(name)
        {
            _units = units;
        }

        public override void SetInputShape(TensorShape inputShape)
        {
            InputShape = inputShape;
            
            // 计算输入大小（展平后的尺寸）
            _inputSize = 1;
            foreach (int dim in inputShape.Dimensions)
                _inputSize *= dim;
                
            // 输出形状为 [_units]
            OutputShape = new TensorShape(_units);
        }

        public override void Initialize(Random random)
        {
            // 初始化权重和偏置
            _weights = new Tensor(_units, _inputSize);
            _biases = new Tensor(_units);
            
            // 使用Xavier初始化
            float scale = (float)Math.Sqrt(2.0 / _inputSize);
            _weights.Randomize(-scale, scale, random);
            _biases.Fill(0f);
        }

        public override ITensor Forward(ITensor input, bool isTraining = true)
        {
            _input = isTraining ? input.Clone() : null;
            
            var output = new Tensor(_units);
            
            // 计算输出: output = weights * input + bias
            for (int i = 0; i < _units; i++)
            {
                float sum = _biases[i];
                for (int j = 0; j < _inputSize; j++)
                {
                    sum += _weights[i, j] * input.Data[j];
                }
                output[i] = sum;
            }
            
            return output;
        }
        private Tensor weightGradients;
        private Tensor biasGradients;
        public override ITensor Backward(ITensor gradient, float learningRate)
        {
            // 计算权重和偏置的梯度
            weightGradients = new Tensor(_units, _inputSize);
            biasGradients = new Tensor(_units);
            
            // 计算偏置梯度
            for (int i = 0; i < _units; i++)
            {
                biasGradients[i] = gradient[i];
            }
            
            // 计算权重梯度
            for (int i = 0; i < _units; i++)
            {
                for (int j = 0; j < _inputSize; j++)
                {
                    weightGradients[i, j] = gradient[i] * _input.Data[j];
                }
            }
            
            // 计算输入梯度（用于前一层的反向传播）
            var inputGradient = new Tensor(InputShape.Dimensions);
            for (int j = 0; j < _inputSize; j++)
            {
                float sum = 0;
                for (int i = 0; i < _units; i++)
                {
                    sum += _weights[i, j] * gradient[i];
                }
                inputGradient.Data[j] = sum;
            }           
            
            return inputGradient;
        }
               

        public override void UpdateParameters(IOptimizer optimizer)
        {
            optimizer.UpdateParameter(_weights, weightGradients);
            optimizer.UpdateParameter(_biases, biasGradients);
        }
    }
}
