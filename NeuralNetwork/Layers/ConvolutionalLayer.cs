using System;

namespace NeuralNetwork.Layers
{
    /// <summary>
    /// 卷积层
    /// </summary>
    public class ConvolutionalLayer : ILayer
    {
        private readonly int _inputDepth;
        private readonly int _outputDepth;
        private readonly int _kernelSize;
        private readonly int _stride;
        private readonly PaddingType _padding;
        
        private float[,,,] _kernels;  // [outputDepth, inputDepth, kernelHeight, kernelWidth]
        private float[] _biases;
        
        private float[,,] _inputCache;  // 缓存输入用于反向传播
        
        public string Name { get; set; }
        public int ParameterCount => _outputDepth * _inputDepth * _kernelSize * _kernelSize + _outputDepth;

        /// <summary>
        /// 初始化卷积层
        /// </summary>
        /// <param name="inputDepth">输入深度</param>
        /// <param name="outputDepth">输出深度</param>
        /// <param name="kernelSize">卷积核大小</param>
        /// <param name="stride">步长</param>
        /// <param name="padding">填充类型</param>
        public ConvolutionalLayer(int inputDepth, int outputDepth, int kernelSize, int stride = 1, PaddingType padding = PaddingType.Valid)
        {
            _inputDepth = inputDepth;
            _outputDepth = outputDepth;
            _kernelSize = kernelSize;
            _stride = stride;
            _padding = padding;
            Name = $"Conv_{inputDepth}x{outputDepth}_{kernelSize}x{kernelSize}";
            
            InitializeParameters();
        }
        
        /// <summary>
        /// 初始化权重和偏置参数
        /// </summary>
        private void InitializeParameters()
        {
            var random = new Random();
            float scale = (float)Math.Sqrt(1.0 / (_inputDepth * _kernelSize * _kernelSize));
            
            // 初始化卷积核
            _kernels = new float[_outputDepth, _inputDepth, _kernelSize, _kernelSize];
            for (int o = 0; o < _outputDepth; o++)
            {
                for (int i = 0; i < _inputDepth; i++)
                {
                    for (int h = 0; h < _kernelSize; h++)
                    {
                        for (int w = 0; w < _kernelSize; w++)
                        {
                            // 使用 Xavier 初始化
                            _kernels[o, i, h, w] = (float)(random.NextDouble() * 2 - 1) * scale;
                        }
                    }
                }
            }
            
            // 初始化偏置
            _biases = new float[_outputDepth];
            for (int o = 0; o < _outputDepth; o++)
            {
                _biases[o] = 0;
            }
        }
        
        /// <summary>
        /// 前向传播
        /// </summary>
        public float[,,] Forward(float[,,] input)
        {
            _inputCache = (float[,,])input.Clone();
            
            int inputDepth = input.GetLength(0);
            int inputHeight = input.GetLength(1);
            int inputWidth = input.GetLength(2);
            
            // 计算填充大小
            int paddingSize = _padding == PaddingType.Same ? (_kernelSize - 1) / 2 : 0;
            
            // 计算输出尺寸
            int outputHeight = (inputHeight + 2 * paddingSize - _kernelSize) / _stride + 1;
            int outputWidth = (inputWidth + 2 * paddingSize - _kernelSize) / _stride + 1;
            
            // 创建填充后的输入
            float[,,] paddedInput = AddPadding(input, paddingSize);
            
            // 初始化输出
            float[,,] output = new float[_outputDepth, outputHeight, outputWidth];
            
            // 执行卷积操作
            for (int o = 0; o < _outputDepth; o++)
            {
                for (int h = 0; h < outputHeight; h++)
                {
                    for (int w = 0; w < outputWidth; w++)
                    {
                        // 计算当前感受野在输入上的起始位置
                        int startH = h * _stride;
                        int startW = w * _stride;
                        
                        // 计算卷积值
                        float sum = 0;
                        for (int i = 0; i < _inputDepth; i++)
                        {
                            for (int kh = 0; kh < _kernelSize; kh++)
                            {
                                for (int kw = 0; kw < _kernelSize; kw++)
                                {
                                    sum += paddedInput[i, startH + kh, startW + kw] * _kernels[o, i, kh, kw];
                                }
                            }
                        }
                        
                        // 添加偏置
                        output[o, h, w] = sum + _biases[o];
                    }
                }
            }
            
            return output;
        }
        
        /// <summary>
        /// 反向传播
        /// </summary>
        public float[,,] Backward(float[,,] outputGradient, float learningRate)
        {
            int inputDepth = _inputCache.GetLength(0);
            int inputHeight = _inputCache.GetLength(1);
            int inputWidth = _inputCache.GetLength(2);
            
            int outputDepth = outputGradient.GetLength(0);
            int outputHeight = outputGradient.GetLength(1);
            int outputWidth = outputGradient.GetLength(2);
            
            // 计算填充大小
            int paddingSize = _padding == PaddingType.Same ? (_kernelSize - 1) / 2 : 0;
            
            // 创建填充后的输入
            float[,,] paddedInput = AddPadding(_inputCache, paddingSize);
            
            // 初始化梯度
            float[,,,] kernelGradients = new float[_outputDepth, _inputDepth, _kernelSize, _kernelSize];
            float[] biasGradients = new float[_outputDepth];
            float[,,] inputGradient = new float[inputDepth, inputHeight, inputWidth];
            float[,,] paddedInputGradient = AddPadding(inputGradient, paddingSize); // 带填充的输入梯度
            
            // 计算梯度
            for (int o = 0; o < _outputDepth; o++)
            {
                // 计算偏置梯度
                for (int h = 0; h < outputHeight; h++)
                {
                    for (int w = 0; w < outputWidth; w++)
                    {
                        biasGradients[o] += outputGradient[o, h, w];
                    }
                }
                
                // 计算卷积核梯度和输入梯度
                for (int i = 0; i < _inputDepth; i++)
                {
                    for (int h = 0; h < outputHeight; h++)
                    {
                        for (int w = 0; w < outputWidth; w++)
                        {
                            int startH = h * _stride;
                            int startW = w * _stride;
                            
                            // 卷积核梯度
                            for (int kh = 0; kh < _kernelSize; kh++)
                            {
                                for (int kw = 0; kw < _kernelSize; kw++)
                                {
                                    kernelGradients[o, i, kh, kw] += 
                                        paddedInput[i, startH + kh, startW + kw] * outputGradient[o, h, w];
                                }
                            }
                            
                            // 输入梯度
                            for (int kh = 0; kh < _kernelSize; kh++)
                            {
                                for (int kw = 0; kw < _kernelSize; kw++)
                                {
                                    paddedInputGradient[i, startH + kh, startW + kw] += 
                                        _kernels[o, i, kh, kw] * outputGradient[o, h, w];
                                }
                            }
                        }
                    }
                }
            }
            
            // 移除填充，得到实际输入梯度
            for (int d = 0; d < inputDepth; d++)
            {
                for (int h = 0; h < inputHeight; h++)
                {
                    for (int w = 0; w < inputWidth; w++)
                    {
                        inputGradient[d, h, w] = paddedInputGradient[d, h + paddingSize, w + paddingSize];
                    }
                }
            }
            
            // 更新参数
            UpdateParameters(kernelGradients, biasGradients, learningRate);
            
            return inputGradient;
        }
        
        /// <summary>
        /// 更新卷积核和偏置参数
        /// </summary>
        private void UpdateParameters(float[,,,] kernelGradients, float[] biasGradients, float learningRate)
        {
            // 更新卷积核
            for (int o = 0; o < _outputDepth; o++)
            {
                for (int i = 0; i < _inputDepth; i++)
                {
                    for (int h = 0; h < _kernelSize; h++)
                    {
                        for (int w = 0; w < _kernelSize; w++)
                        {
                            _kernels[o, i, h, w] -= learningRate * kernelGradients[o, i, h, w];
                        }
                    }
                }
            }
            
            // 更新偏置
            for (int o = 0; o < _outputDepth; o++)
            {
                _biases[o] -= learningRate * biasGradients[o];
            }
        }
        
        /// <summary>
        /// 为输入添加填充
        /// </summary>
        private float[,,] AddPadding(float[,,] input, int paddingSize)
        {
            if (paddingSize <= 0)
                return input;
                
            int depth = input.GetLength(0);
            int height = input.GetLength(1);
            int width = input.GetLength(2);
            
            int paddedHeight = height + 2 * paddingSize;
            int paddedWidth = width + 2 * paddingSize;
            
            float[,,] paddedInput = new float[depth, paddedHeight, paddedWidth];
            
            for (int d = 0; d < depth; d++)
            {
                for (int h = 0; h < height; h++)
                {
                    for (int w = 0; w < width; w++)
                    {
                        paddedInput[d, h + paddingSize, w + paddingSize] = input[d, h, w];
                    }
                }
            }
            
            return paddedInput;
        }
    }
}
