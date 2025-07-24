using NeuralNetworkLibrary.Core;
using NeuralNetworkLibrary.Optimizers;
using System;

namespace NeuralNetworkLibrary.Layers
{
    /// <summary>
    /// Dropout层实现，用于防止过拟合，同时支持全连接层和卷积层
    /// </summary>
    public class DropoutLayer : BaseLayer
    {
        private float _dropoutRate;
        private bool[,] _mask; // 存储dropout掩码，适用于2D输入（批次×特征 或 通道×特征）
        private Random _random;
        private int[] _inputDimensions; // 存储输入维度信息，用于处理不同形状的输入

        public DropoutLayer(float dropoutRate = 0.5f, string name = "Dropout") : base(name)
        {
            if (dropoutRate < 0 || dropoutRate >= 1)
                throw new ArgumentException("Dropout rate must be between 0 and 1 (inclusive of 0, exclusive of 1)");

            _dropoutRate = dropoutRate;
            _random = new Random();
        }

        public override void SetInputShape(TensorShape inputShape)
        {
            InputShape = inputShape;
            OutputShape = inputShape.Clone(); // Dropout不改变形状
            _inputDimensions = inputShape.Dimensions;
        }

        public override void Initialize(Random random)
        {
            _random = random ?? new Random();
            // 掩码在Forward中动态生成，这里不需要初始化
        }

        public override ITensor Forward(ITensor input, bool isTraining = true)
        {
            var output = input.Clone();

            if (isTraining && _dropoutRate > 0)
            {
                float keepProbability = 1 - _dropoutRate;

                // 处理全连接层输入 (通常是 批次×特征 或 特征)
                if (_inputDimensions.Length == 1)
                {
                    // 1D输入: [特征数]
                    Handle1DInput(input, output, keepProbability);
                }
                else if (_inputDimensions.Length == 2)
                {
                    // 2D输入: [批次×特征] 或 [通道×特征]
                    Handle2DInput(input, output, keepProbability);
                }
                else if (_inputDimensions.Length == 3)
                {
                    // 3D输入: [通道×高度×宽度] - 适用于卷积层
                    Handle3DInput(input, output, keepProbability);
                }
                else
                {
                    throw new NotSupportedException($"DropoutLayer不支持{_inputDimensions.Length}维输入");
                }
            }

            return output;
        }

        private void Handle1DInput(ITensor input, ITensor output, float keepProbability)
        {
            int features = _inputDimensions[0];
            _mask = new bool[1, features]; // 使用1作为第一维

            for (int i = 0; i < features; i++)
            {
                // 生成掩码：保留概率为keepProbability
                bool keep = _random.NextDouble() < keepProbability;
                _mask[0, i] = keep;

                // 应用掩码并缩放（除以keepProbability以保持期望值不变）
                output[i] = keep ? output[i] / keepProbability : 0;
            }
        }

        private void Handle2DInput(ITensor input, ITensor output, float keepProbability)
        {
            int dim1 = _inputDimensions[0]; // 批次或通道
            int dim2 = _inputDimensions[1]; // 特征数

            _mask = new bool[dim1, dim2];

            for (int i = 0; i < dim1; i++)
            {
                for (int j = 0; j < dim2; j++)
                {
                    // 生成掩码：保留概率为keepProbability
                    bool keep = _random.NextDouble() < keepProbability;
                    _mask[i, j] = keep;

                    // 应用掩码并缩放（除以keepProbability以保持期望值不变）
                    output[i, j] = keep ? output[i, j] / keepProbability : 0;
                }
            }
        }

        private void Handle3DInput(ITensor input, ITensor output, float keepProbability)
        {
            int channels = _inputDimensions[0];
            int height = _inputDimensions[1];
            int width = _inputDimensions[2];
            int totalFeatures = height * width;

            _mask = new bool[channels, totalFeatures];

            for (int c = 0; c < channels; c++)
            {
                for (int i = 0; i < totalFeatures; i++)
                {
                    // 生成掩码：保留概率为keepProbability
                    bool keep = _random.NextDouble() < keepProbability;
                    _mask[c, i] = keep;

                    // 计算高度和宽度索引
                    int h = i / width;
                    int w = i % width;

                    // 应用掩码并缩放
                    output[c, h, w] = keep ? output[c, h, w] / keepProbability : 0;
                }
            }
        }

        public override ITensor Backward(ITensor gradient, float learningRate)
        {
            var inputGradient = gradient.Clone();

            if (_dropoutRate > 0 && _mask != null)
            {
                float keepProbability = 1 - _dropoutRate;

                if (_inputDimensions.Length == 1)
                {
                    // 处理1D输入的反向传播
                    int features = _inputDimensions[0];
                    for (int i = 0; i < features; i++)
                    {
                        inputGradient[i] = _mask[0, i] ? inputGradient[i] / keepProbability : 0;
                    }
                }
                else if (_inputDimensions.Length == 2)
                {
                    // 处理2D输入的反向传播（全连接层常用）
                    int dim1 = _inputDimensions[0];
                    int dim2 = _inputDimensions[1];

                    for (int i = 0; i < dim1; i++)
                    {
                        for (int j = 0; j < dim2; j++)
                        {
                            inputGradient[i, j] = _mask[i, j] ? inputGradient[i, j] / keepProbability : 0;
                        }
                    }
                }
                else if (_inputDimensions.Length == 3)
                {
                    // 处理3D输入的反向传播
                    int channels = _inputDimensions[0];
                    int height = _inputDimensions[1];
                    int width = _inputDimensions[2];
                    int totalFeatures = height * width;

                    for (int c = 0; c < channels; c++)
                    {
                        for (int i = 0; i < totalFeatures; i++)
                        {
                            int h = i / width;
                            int w = i % width;

                            inputGradient[c, h, w] = _mask[c, i] ? inputGradient[c, h, w] / keepProbability : 0;
                        }
                    }
                }
            }

            return inputGradient;
        }

        public override void UpdateParameters(IOptimizer optimizer)
        {
            // Dropout层没有可学习的参数，所以不需要更新
        }
    }
}
