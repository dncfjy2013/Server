using System;

namespace NeuralNetwork.Layers
{
    /// <summary>
    /// 池化层
    /// </summary>
    public class PoolingLayer : ILayer
    {
        private readonly int _poolSize;
        private readonly int _stride;
        private readonly PoolingType _poolingType;
        
        // 用于反向传播的缓存
        private int[,,,] _maxIndices;  // [depth, outputH, outputW, 2] 存储最大值的位置 (h, w)
        private float[,,] _inputCache;
        
        public string Name { get; set; }
        public int ParameterCount => 0;  // 池化层没有可训练参数

        /// <summary>
        /// 初始化池化层
        /// </summary>
        /// <param name="poolSize">池化窗口大小</param>
        /// <param name="stride">步长</param>
        /// <param name="poolingType">池化类型</param>
        public PoolingLayer(int poolSize, int stride = -1, PoolingType poolingType = PoolingType.Max)
        {
            _poolSize = poolSize;
            _stride = stride <= 0 ? poolSize : stride;  // 默认步长等于池化窗口大小
            _poolingType = poolingType;
            Name = $"{poolingType}Pool_{poolSize}x{poolSize}";
        }
        
        /// <summary>
        /// 前向传播
        /// </summary>
        public float[,,] Forward(float[,,] input)
        {
            _inputCache = (float[,,])input.Clone();
            
            int depth = input.GetLength(0);
            int height = input.GetLength(1);
            int width = input.GetLength(2);
            
            // 计算输出尺寸
            int outputHeight = (height - _poolSize) / _stride + 1;
            int outputWidth = (width - _poolSize) / _stride + 1;
            
            // 初始化输出
            float[,,] output = new float[depth, outputHeight, outputWidth];
            
            if (_poolingType == PoolingType.Max)
            {
                // 为最大值池化初始化索引缓存
                _maxIndices = new int[depth, outputHeight, outputWidth, 2];
                
                // 最大值池化
                for (int d = 0; d < depth; d++)
                {
                    for (int h = 0; h < outputHeight; h++)
                    {
                        for (int w = 0; w < outputWidth; w++)
                        {
                            int startH = h * _stride;
                            int startW = w * _stride;
                            
                            float maxVal = float.MinValue;
                            int maxH = 0, maxW = 0;
                            
                            // 遍历池化窗口
                            for (int ph = 0; ph < _poolSize; ph++)
                            {
                                for (int pw = 0; pw < _poolSize; pw++)
                                {
                                    float val = input[d, startH + ph, startW + pw];
                                    if (val > maxVal)
                                    {
                                        maxVal = val;
                                        maxH = startH + ph;
                                        maxW = startW + pw;
                                    }
                                }
                            }
                            
                            output[d, h, w] = maxVal;
                            _maxIndices[d, h, w, 0] = maxH;  // 记录最大值的高度索引
                            _maxIndices[d, h, w, 1] = maxW;  // 记录最大值的宽度索引
                        }
                    }
                }
            }
            else if (_poolingType == PoolingType.Average)
            {
                // 平均池化
                for (int d = 0; d < depth; d++)
                {
                    for (int h = 0; h < outputHeight; h++)
                    {
                        for (int w = 0; w < outputWidth; w++)
                        {
                            int startH = h * _stride;
                            int startW = w * _stride;
                            
                            float sum = 0;
                            
                            // 遍历池化窗口
                            for (int ph = 0; ph < _poolSize; ph++)
                            {
                                for (int pw = 0; pw < _poolSize; pw++)
                                {
                                    sum += input[d, startH + ph, startW + pw];
                                }
                            }
                            
                            output[d, h, w] = sum / (_poolSize * _poolSize);
                        }
                    }
                }
            }
            else if (_poolingType == PoolingType.Sum)
            {
                // 求和池化
                for (int d = 0; d < depth; d++)
                {
                    for (int h = 0; h < outputHeight; h++)
                    {
                        for (int w = 0; w < outputWidth; w++)
                        {
                            int startH = h * _stride;
                            int startW = w * _stride;
                            
                            float sum = 0;
                            
                            // 遍历池化窗口
                            for (int ph = 0; ph < _poolSize; ph++)
                            {
                                for (int pw = 0; pw < _poolSize; pw++)
                                {
                                    sum += input[d, startH + ph, startW + pw];
                                }
                            }
                            
                            output[d, h, w] = sum;
                        }
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
            int depth = _inputCache.GetLength(0);
            int inputHeight = _inputCache.GetLength(1);
            int inputWidth = _inputCache.GetLength(2);
            
            int outputHeight = outputGradient.GetLength(1);
            int outputWidth = outputGradient.GetLength(2);
            
            // 初始化输入梯度
            float[,,] inputGradient = new float[depth, inputHeight, inputWidth];
            
            if (_poolingType == PoolingType.Max)
            {
                // 最大值池化反向传播
                for (int d = 0; d < depth; d++)
                {
                    for (int h = 0; h < outputHeight; h++)
                    {
                        for (int w = 0; w < outputWidth; w++)
                        {
                            // 只将梯度传递给前向传播中最大值所在的位置
                            int maxH = _maxIndices[d, h, w, 0];
                            int maxW = _maxIndices[d, h, w, 1];
                            inputGradient[d, maxH, maxW] += outputGradient[d, h, w];
                        }
                    }
                }
            }
            else if (_poolingType == PoolingType.Average)
            {
                // 平均池化反向传播
                for (int d = 0; d < depth; d++)
                {
                    for (int h = 0; h < outputHeight; h++)
                    {
                        for (int w = 0; w < outputWidth; w++)
                        {
                            int startH = h * _stride;
                            int startW = w * _stride;
                            
                            // 将梯度平均分配到池化窗口中的所有位置
                            float gradient = outputGradient[d, h, w] / (_poolSize * _poolSize);
                            
                            for (int ph = 0; ph < _poolSize; ph++)
                            {
                                for (int pw = 0; pw < _poolSize; pw++)
                                {
                                    inputGradient[d, startH + ph, startW + pw] += gradient;
                                }
                            }
                        }
                    }
                }
            }
            else if (_poolingType == PoolingType.Sum)
            {
                // 求和池化反向传播
                for (int d = 0; d < depth; d++)
                {
                    for (int h = 0; h < outputHeight; h++)
                    {
                        for (int w = 0; w < outputWidth; w++)
                        {
                            int startH = h * _stride;
                            int startW = w * _stride;
                            
                            // 将相同的梯度分配到池化窗口中的所有位置
                            float gradient = outputGradient[d, h, w];
                            
                            for (int ph = 0; ph < _poolSize; ph++)
                            {
                                for (int pw = 0; pw < _poolSize; pw++)
                                {
                                    inputGradient[d, startH + ph, startW + pw] += gradient;
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
