using NeuralNetworkLibrary.Core;
using NeuralNetworkLibrary.Optimizers;
using System;

namespace NeuralNetworkLibrary.Layers
{
    public enum PoolingType
    {
        Max,
        Average
    }
    
    /// <summary>
    /// 池化层实现
    /// </summary>
    public class PoolingLayer : BaseLayer
    {
        private int _poolSize;
        private int _stride;
        private PoolingType _poolingType;
        private int[,,,] _maxIndices; // 用于最大池化的反向传播 [channels, outputH, outputW, 2] 存储最大值的坐标
        
        public PoolingLayer(int poolSize, int stride = -1, PoolingType poolingType = PoolingType.Max, string name = "Pool") 
            : base(name)
        {
            _poolSize = poolSize;
            _stride = stride == -1 ? poolSize : stride; // 默认步长等于池化大小
            _poolingType = poolingType;
        }

        public override void SetInputShape(TensorShape inputShape)
        {
            // 输入形状: [channels, height, width]
            InputShape = inputShape;
            
            // 计算输出形状
            int outputHeight = (inputShape[1] - _poolSize) / _stride + 1;
            int outputWidth = (inputShape[2] - _poolSize) / _stride + 1;
            
            OutputShape = new TensorShape(inputShape[0], outputHeight, outputWidth);
        }

        public override void Initialize(Random random)
        {
            // 池化层没有参数需要初始化
            if (_poolingType == PoolingType.Max)
            {
                // 为最大池化初始化索引存储
                _maxIndices = new int[InputShape[0], OutputShape[1], OutputShape[2], 2];
            }
        }

        public override ITensor Forward(ITensor input, bool isTraining = true)
        {
            int channels = InputShape[0];
            int inputHeight = InputShape[1];
            int inputWidth = InputShape[2];
            int outputHeight = OutputShape[1];
            int outputWidth = OutputShape[2];
            
            var output = new Tensor(OutputShape.Dimensions);
            
            for (int c = 0; c < channels; c++)
            {
                for (int h = 0; h < outputHeight; h++)
                {
                    for (int w = 0; w < outputWidth; w++)
                    {
                        int startH = h * _stride;
                        int startW = w * _stride;
                        int endH = startH + _poolSize;
                        int endW = startW + _poolSize;
                        
                        if (_poolingType == PoolingType.Max)
                        {
                            // 最大池化
                            float maxVal = float.MinValue;
                            int maxH = 0, maxW = 0;
                            
                            for (int ph = startH; ph < endH && ph < inputHeight; ph++)
                            {
                                for (int pw = startW; pw < endW && pw < inputWidth; pw++)
                                {
                                    float val = input[c, ph, pw];
                                    if (val > maxVal)
                                    {
                                        maxVal = val;
                                        maxH = ph;
                                        maxW = pw;
                                    }
                                }
                            }
                            
                            output[c, h, w] = maxVal;
                            
                            // 仅在训练时存储最大值索引用于反向传播
                            if (isTraining)
                            {
                                _maxIndices[c, h, w, 0] = maxH;
                                _maxIndices[c, h, w, 1] = maxW;
                            }
                        }
                        else
                        {
                            // 平均池化
                            float sum = 0;
                            int count = 0;
                            
                            for (int ph = startH; ph < endH && ph < inputHeight; ph++)
                            {
                                for (int pw = startW; pw < endW && pw < inputWidth; pw++)
                                {
                                    sum += input[c, ph, pw];
                                    count++;
                                }
                            }
                            
                            output[c, h, w] = sum / count;
                        }
                    }
                }
            }
            
            return output;
        }

        public override ITensor Backward(ITensor gradient, float learningRate)
        {
            int channels = InputShape[0];
            int inputHeight = InputShape[1];
            int inputWidth = InputShape[2];
            int outputHeight = OutputShape[1];
            int outputWidth = OutputShape[2];
            
            var inputGradient = new Tensor(InputShape.Dimensions);
            
            for (int c = 0; c < channels; c++)
            {
                for (int h = 0; h < outputHeight; h++)
                {
                    for (int w = 0; w < outputWidth; w++)
                    {
                        float gradVal = gradient[c, h, w];
                        
                        if (_poolingType == PoolingType.Max)
                        {
                            // 最大池化反向传播 - 只将梯度传递给最大值的位置
                            int maxH = _maxIndices[c, h, w, 0];
                            int maxW = _maxIndices[c, h, w, 1];
                            inputGradient[c, maxH, maxW] += gradVal;
                        }
                        else
                        {
                            // 平均池化反向传播 - 将梯度平均分配到池化区域
                            int startH = h * _stride;
                            int startW = w * _stride;
                            int endH = startH + _poolSize;
                            int endW = startW + _poolSize;
                            
                            int count = 0;
                            for (int ph = startH; ph < endH && ph < inputHeight; ph++)
                            {
                                for (int pw = startW; pw < endW && pw < inputWidth; pw++)
                                {
                                    count++;
                                }
                            }
                            
                            float avgGrad = gradVal / count;
                            
                            for (int ph = startH; ph < endH && ph < inputHeight; ph++)
                            {
                                for (int pw = startW; pw < endW && pw < inputWidth; pw++)
                                {
                                    inputGradient[c, ph, pw] += avgGrad;
                                }
                            }
                        }
                    }
                }
            }
            
            return inputGradient;
        }

    }
}
