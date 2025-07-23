using System;

namespace NeuralNetwork.Layers
{
    /// <summary>
    /// 全连接层（密集层）
    /// </summary>
    public class DenseLayer : ILayer
    {
        private readonly int _inputSize;
        private readonly int _outputSize;
        private readonly ActivationType _activation;
        
        private float[,] _weights;  // [outputSize, inputSize]
        private float[] _biases;
        
        private float[,,] _inputCache;  // 缓存输入用于反向传播
        private float[,,] _preActivationCache;  // 缓存激活前的值用于反向传播
        
        public string Name { get; set; }
        public int ParameterCount => _inputSize * _outputSize + _outputSize;

        /// <summary>
        /// 初始化全连接层
        /// </summary>
        /// <param name="inputSize">输入大小</param>
        /// <param name="outputSize">输出大小</param>
        /// <param name="activation">激活函数类型</param>
        public DenseLayer(int inputSize, int outputSize, ActivationType activation = ActivationType.ReLU)
        {
            _inputSize = inputSize;
            _outputSize = outputSize;
            _activation = activation;
            Name = $"Dense_{inputSize}x{outputSize}_{activation}";
            
            InitializeParameters();
        }
        
        /// <summary>
        /// 初始化权重和偏置参数
        /// </summary>
        private void InitializeParameters()
        {
            var random = new Random();
            float scale = (float)Math.Sqrt(1.0 / _inputSize);  // Xavier初始化
            
            // 初始化权重
            _weights = new float[_outputSize, _inputSize];
            for (int o = 0; o < _outputSize; o++)
            {
                for (int i = 0; i < _inputSize; i++)
                {
                    _weights[o, i] = (float)(random.NextDouble() * 2 - 1) * scale;
                }
            }
            
            // 初始化偏置
            _biases = new float[_outputSize];
            for (int o = 0; o < _outputSize; o++)
            {
                _biases[o] = 0;
            }
        }
        
        /// <summary>
        /// 前向传播
        /// </summary>
        public float[,,] Forward(float[,,] input)
        {
            // 确保输入是扁平化的 (depth, 1, 1)
            if (input.GetLength(1) != 1 || input.GetLength(2) != 1)
            {
                throw new ArgumentException("Dense layer input must be a flattened array (depth, 1, 1)");
            }
            
            _inputCache = (float[,,])input.Clone();
            
            // 计算激活前的值
            float[,,] preActivation = new float[_outputSize, 1, 1];
            for (int o = 0; o < _outputSize; o++)
            {
                float sum = _biases[o];
                for (int i = 0; i < _inputSize; i++)
                {
                    sum += _weights[o, i] * input[i, 0, 0];
                }
                preActivation[o, 0, 0] = sum;
            }
            
            // 缓存激活前的值用于反向传播
            _preActivationCache = preActivation;
            
            // 应用激活函数
            if (_activation == ActivationType.Softmax)
            {
                return ActivationFunctions.Softmax(preActivation);
            }
            return ActivationFunctions.ApplyActivation(preActivation, _activation);
        }
        
        /// <summary>
        /// 反向传播
        /// </summary>
        public float[,,] Backward(float[,,] outputGradient, float learningRate)
        {
            // 计算激活函数的导数
            float[,,] activationDerivative = ActivationFunctions.ApplyActivationDerivative(
                _preActivationCache, _activation);
            
            // 计算激活前的梯度
            float[,,] preActivationGradient = new float[_outputSize, 1, 1];
            for (int o = 0; o < _outputSize; o++)
            {
                preActivationGradient[o, 0, 0] = outputGradient[o, 0, 0] * activationDerivative[o, 0, 0];
            }
            
            // 计算权重和偏置的梯度
            float[,] weightGradients = new float[_outputSize, _inputSize];
            float[] biasGradients = new float[_outputSize];
            
            for (int o = 0; o < _outputSize; o++)
            {
                biasGradients[o] = preActivationGradient[o, 0, 0];
                
                for (int i = 0; i < _inputSize; i++)
                {
                    weightGradients[o, i] = preActivationGradient[o, 0, 0] * _inputCache[i, 0, 0];
                }
            }
            
            // 计算输入梯度
            float[,,] inputGradient = new float[_inputSize, 1, 1];
            for (int i = 0; i < _inputSize; i++)
            {
                float sum = 0;
                for (int o = 0; o < _outputSize; o++)
                {
                    sum += preActivationGradient[o, 0, 0] * _weights[o, i];
                }
                inputGradient[i, 0, 0] = sum;
            }
            
            // 更新参数
            UpdateParameters(weightGradients, biasGradients, learningRate);
            
            return inputGradient;
        }
        
        /// <summary>
        /// 更新权重和偏置参数
        /// </summary>
        private void UpdateParameters(float[,] weightGradients, float[] biasGradients, float learningRate)
        {
            // 更新权重
            for (int o = 0; o < _outputSize; o++)
            {
                for (int i = 0; i < _inputSize; i++)
                {
                    _weights[o, i] -= learningRate * weightGradients[o, i];
                }
            }
            
            // 更新偏置
            for (int o = 0; o < _outputSize; o++)
            {
                _biases[o] -= learningRate * biasGradients[o];
            }
        }
    }
}
