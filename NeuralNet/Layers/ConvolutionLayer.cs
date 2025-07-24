using NeuralNetworkLibrary.Core;
using NeuralNetworkLibrary.Optimizers;
using System;

namespace NeuralNetworkLibrary.Layers
{
    /// <summary>
    /// 卷积层实现
    /// </summary>
    public class ConvolutionLayer : BaseLayer
    {
        private int _kernelSize;
        private int _filters;
        private int _stride;
        private int _padding;
        
        private ITensor _kernels;  // [filters, inputChannels, kernelSize, kernelSize]
        private ITensor _biases;   // [filters]
        private ITensor _input;    // 缓存前向传播的输入用于反向传播

        private Tensor kernelGradients;
        private Tensor biasGradients;

        public override bool HasParameters => true;

        public ConvolutionLayer(int kernelSize, int filters, int stride = 1, int padding = 1, string name = "Conv") 
            : base(name)
        {
            _kernelSize = kernelSize;
            _filters = filters;
            _stride = stride;
            _padding = padding;
        }

        public override void SetInputShape(TensorShape inputShape)
        {
            // 输入形状: [channels, height, width]
            InputShape = inputShape;
            
            // 计算输出形状
            int outputHeight = (inputShape[1] + 2 * _padding - _kernelSize) / _stride + 1;
            int outputWidth = (inputShape[2] + 2 * _padding - _kernelSize) / _stride + 1;
            
            OutputShape = new TensorShape(_filters, outputHeight, outputWidth);
        }

        public override void Initialize(Random random)
        {
            // 初始化卷积核和偏置
            int inputChannels = InputShape[0];
            _kernels = new Tensor(_filters, inputChannels, _kernelSize, _kernelSize);
            
            // 使用Xavier初始化
            float scale = (float)Math.Sqrt(2.0 / (inputChannels * _kernelSize * _kernelSize));
            _kernels.Randomize(-scale, scale, random);
            
            _biases = new Tensor(_filters);
            _biases.Fill(0f);
        }

        public override ITensor Forward(ITensor input, bool isTraining = true)
        {
            _input = isTraining ? input.Clone() : null; // 仅在训练时缓存输入
            
            int inputChannels = InputShape[0];
            int inputHeight = InputShape[1];
            int inputWidth = InputShape[2];
            int outputHeight = OutputShape[1];
            int outputWidth = OutputShape[2];
            
            var output = new Tensor(OutputShape.Dimensions);
            
            // 执行卷积操作
            for (int f = 0; f < _filters; f++)
            {
                for (int h = 0; h < outputHeight; h++)
                {
                    for (int w = 0; w < outputWidth; w++)
                    {
                        float sum = _biases[f]; // 添加偏置
                        
                        // 卷积计算
                        for (int c = 0; c < inputChannels; c++)
                        {
                            for (int kh = 0; kh < _kernelSize; kh++)
                            {
                                for (int kw = 0; kw < _kernelSize; kw++)
                                {
                                    int inputH = h * _stride + kh - _padding;
                                    int inputW = w * _stride + kw - _padding;
                                    
                                    // 检查是否在有效范围内
                                    if (inputH >= 0 && inputH < inputHeight && 
                                        inputW >= 0 && inputW < inputWidth)
                                    {
                                        sum += input[c, inputH, inputW] * _kernels[f, c, kh, kw];
                                    }
                                }
                            }
                        }
                        
                        output[f, h, w] = sum;
                    }
                }
            }
            
            return output;
        }

        public override ITensor Backward(ITensor gradient, float learningRate)
        {
            int inputChannels = InputShape[0];
            int inputHeight = InputShape[1];
            int inputWidth = InputShape[2];
            int outputHeight = OutputShape[1];
            int outputWidth = OutputShape[2];
            
            // 创建用于存储权重和偏置梯度的张量
            kernelGradients = new Tensor(_filters, inputChannels, _kernelSize, _kernelSize);
            biasGradients = new Tensor(_filters);
            
            // 计算输入的梯度（用于前一层的反向传播）
            var inputGradient = new Tensor(InputShape.Dimensions);
            
            // 计算梯度
            for (int f = 0; f < _filters; f++)
            {
                // 计算偏置梯度
                float biasSum = 0;
                for (int h = 0; h < outputHeight; h++)
                {
                    for (int w = 0; w < outputWidth; w++)
                    {
                        biasSum += gradient[f, h, w];
                    }
                }
                biasGradients[f] = biasSum;
                
                // 计算卷积核梯度
                for (int c = 0; c < inputChannels; c++)
                {
                    for (int kh = 0; kh < _kernelSize; kh++)
                    {
                        for (int kw = 0; kw < _kernelSize; kw++)
                        {
                            float kernelSum = 0;
                            for (int h = 0; h < outputHeight; h++)
                            {
                                for (int w = 0; w < outputWidth; w++)
                                {
                                    int inputH = h * _stride + kh - _padding;
                                    int inputW = w * _stride + kw - _padding;
                                    
                                    if (inputH >= 0 && inputH < inputHeight && 
                                        inputW >= 0 && inputW < inputWidth)
                                    {
                                        kernelSum += _input[c, inputH, inputW] * gradient[f, h, w];
                                    }
                                }
                            }
                            kernelGradients[f, c, kh, kw] = kernelSum;
                        }
                    }
                }
                
                // 计算输入梯度
                for (int c = 0; c < inputChannels; c++)
                {
                    for (int h = 0; h < inputHeight; h++)
                    {
                        for (int w = 0; w < inputWidth; w++)
                        {
                            float sum = 0;
                            for (int kh = 0; kh < _kernelSize; kh++)
                            {
                                for (int kw = 0; kw < _kernelSize; kw++)
                                {
                                    int outputH = (h + _padding - kh) / _stride;
                                    int outputW = (w + _padding - kw) / _stride;
                                    
                                    if (outputH >= 0 && outputH < outputHeight && 
                                        outputW >= 0 && outputW < outputWidth &&
                                        (h + _padding - kh) % _stride == 0 &&
                                        (w + _padding - kw) % _stride == 0)
                                    {
                                        sum += _kernels[f, c, kh, kw] * gradient[f, outputH, outputW];
                                    }
                                }
                            }
                            inputGradient[c, h, w] += sum;
                        }
                    }
                }
            }          
            
            return inputGradient;
        }

        public override void UpdateParameters(IOptimizer optimizer)
        {
            optimizer.UpdateParameter(_kernels, kernelGradients);
            optimizer.UpdateParameter(_biases, biasGradients);
        }
    }
}
