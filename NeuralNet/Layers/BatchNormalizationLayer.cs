using NeuralNetworkLibrary.Core;
using NeuralNetworkLibrary.Optimizers;
using System;

namespace NeuralNetworkLibrary.Layers
{
    /// <summary>
    /// 批归一化层实现
    /// </summary>
    public class BatchNormalizationLayer : BaseLayer
    {
        private float _epsilon = 1e-5f;
        private float _momentum = 0.9f;
        
        private ITensor _gamma;   // 缩放参数
        private ITensor _beta;    // 偏移参数
        private ITensor _movingMean;  // 移动平均值（用于推理）
        private ITensor _movingVariance;  // 移动方差（用于推理）
        
        // 用于反向传播的缓存
        private ITensor _xNorm;
        private ITensor _mean;
        private ITensor _variance;
        private ITensor _input;
        
        public override bool HasParameters => true;

        public BatchNormalizationLayer(float epsilon = 1e-5f, float momentum = 0.9f, string name = "BN") 
            : base(name)
        {
            _epsilon = epsilon;
            _momentum = momentum;
        }

        public override void SetInputShape(TensorShape inputShape)
        {
            InputShape = inputShape;
            OutputShape = inputShape.Clone(); // 批归一化不改变形状
        }

        public override void Initialize(Random random)
        {
            // 对于CNN，通常是按通道归一化
            int channels = InputShape[0];
            
            // 初始化参数
            _gamma = new Tensor(channels);
            _gamma.Fill(1.0f);  // 初始缩放为1
            
            _beta = new Tensor(channels);
            _beta.Fill(0.0f);   // 初始偏移为0
            
            // 初始化移动平均值和方差
            _movingMean = new Tensor(channels);
            _movingMean.Fill(0.0f);
            
            _movingVariance = new Tensor(channels);
            _movingVariance.Fill(1.0f);
        }

        public override ITensor Forward(ITensor input, bool isTraining = true)
        {
            int channels = InputShape[0];
            int height = InputShape[1];
            int width = InputShape[2];
            int spatialSize = height * width;
            
            _input = isTraining ? input.Clone() : null;
            var output = new Tensor(OutputShape.Dimensions);
            
            if (isTraining)
            {
                // 训练模式 - 计算批次的均值和方差
                _mean = new Tensor(channels);
                _variance = new Tensor(channels);
                
                // 计算每个通道的均值
                for (int c = 0; c < channels; c++)
                {
                    float sum = 0;
                    for (int h = 0; h < height; h++)
                    {
                        for (int w = 0; w < width; w++)
                        {
                            sum += input[c, h, w];
                        }
                    }
                    _mean[c] = sum / spatialSize;
                }
                
                // 计算每个通道的方差
                for (int c = 0; c < channels; c++)
                {
                    float sum = 0;
                    for (int h = 0; h < height; h++)
                    {
                        for (int w = 0; w < width; w++)
                        {
                            float diff = input[c, h, w] - _mean[c];
                            sum += diff * diff;
                        }
                    }
                    _variance[c] = sum / spatialSize + _epsilon;
                }
                
                // 计算归一化值
                _xNorm = new Tensor(InputShape.Dimensions);
                for (int c = 0; c < channels; c++)
                {
                    float std = (float)Math.Sqrt(_variance[c]);
                    for (int h = 0; h < height; h++)
                    {
                        for (int w = 0; w < width; w++)
                        {
                            _xNorm[c, h, w] = (input[c, h, w] - _mean[c]) / std;
                            output[c, h, w] = _gamma[c] * _xNorm[c, h, w] + _beta[c];
                        }
                    }
                }
                
                // 更新移动平均值和方差
                for (int c = 0; c < channels; c++)
                {
                    _movingMean[c] = _momentum * _movingMean[c] + (1 - _momentum) * _mean[c];
                    _movingVariance[c] = _momentum * _movingVariance[c] + (1 - _momentum) * _variance[c];
                }
            }
            else
            {
                // 推理模式 - 使用移动平均值和方差
                for (int c = 0; c < channels; c++)
                {
                    float std = (float)Math.Sqrt(_movingVariance[c] + _epsilon);
                    for (int h = 0; h < height; h++)
                    {
                        for (int w = 0; w < width; w++)
                        {
                            output[c, h, w] = _gamma[c] * (input[c, h, w] - _movingMean[c]) / std + _beta[c];
                        }
                    }
                }
            }
            
            return output;
        }

        public override ITensor Backward(ITensor gradient, float learningRate)
        {
            int channels = InputShape[0];
            int height = InputShape[1];
            int width = InputShape[2];
            int spatialSize = height * width;
            
            var inputGradient = new Tensor(InputShape.Dimensions);
            var dGamma = new Tensor(channels);
            var dBeta = new Tensor(channels);
            
            // 计算gamma和beta的梯度
            for (int c = 0; c < channels; c++)
            {
                float sumGamma = 0;
                float sumBeta = 0;
                
                for (int h = 0; h < height; h++)
                {
                    for (int w = 0; w < width; w++)
                    {
                        sumGamma += gradient[c, h, w] * _xNorm[c, h, w];
                        sumBeta += gradient[c, h, w];
                    }
                }
                
                dGamma[c] = sumGamma;
                dBeta[c] = sumBeta;
            }
            
            // 计算输入的梯度
            for (int c = 0; c < channels; c++)
            {
                float std = (float)Math.Sqrt(_variance[c]);
                float varInv = 1.0f / std;
                
                for (int h = 0; h < height; h++)
                {
                    for (int w = 0; w < width; w++)
                    {
                        float term1 = _gamma[c] * varInv * gradient[c, h, w];
                        float term2 = _gamma[c] * varInv / spatialSize;
                        float term3 = (_input[c, h, w] - _mean[c]) * (float)Math.Pow(varInv, 3) * dGamma[c];
                        
                        inputGradient[c, h, w] = term1 - term2 * (dBeta[c] + term3);
                    }
                }
            }
            
            // 更新参数
            for (int c = 0; c < channels; c++)
            {
                _gamma[c] -= learningRate * dGamma[c];
                _beta[c] -= learningRate * dBeta[c];
            }
            
            return inputGradient;
        }

        public override void UpdateParameters(IOptimizer optimizer)
        {
            // 批归一化层的参数更新通常在反向传播中完成
            // 这里可以添加额外的参数更新逻辑，如果需要的话
            // 例如，使用优化器来更新 gamma 和 beta 参数
            //optimizer.Update(_gamma);
            //optimizer.Update(_beta);
        }
    }
}
