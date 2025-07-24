using NeuralNetworkLibrary.Core;
using NeuralNetworkLibrary.Optimizers;
using System;
using System.Numerics;
using System.Threading.Tasks;

namespace NeuralNetworkLibrary.Layers
{
    /// <summary>
    /// 优化的批归一化层，参数更新在反向传播中完成，UpdateParameters为空实现
    /// </summary>
    public class BatchNormalizationLayer : BaseLayer
    {
        private const float _epsilon = 1e-5f;
        private readonly float _momentum;
        private int _channels;       // 输入通道数
        private int _spatialSize;    // 单通道空间元素总数（height * width）
        private readonly int _simdLength;  // SIMD向量长度

        // 可学习参数
        private ITensor _gamma;      // 缩放参数 [channels]
        private ITensor _beta;       // 偏移参数 [channels]
        // 推理用统计量
        private ITensor _movingMean; // 移动均值 [channels]
        private ITensor _movingVar;  // 移动方差 [channels]
        // 训练时缓存（用于反向传播）
        private ITensor _xNorm;      // 归一化后的值 [channels, height, width]
        private ITensor _input;      // 输入缓存 [channels, height, width]
        private ITensor _mean;       // 批次均值 [channels]
        private ITensor _var;        // 批次方差 [channels]

        public override bool HasParameters => true;

        public BatchNormalizationLayer(float momentum = 0.9f, string name = "BN") : base(name)
        {
            _momentum = momentum;
            _simdLength = Vector<float>.Count;
        }

        public override void SetInputShape(TensorShape inputShape)
        {
            InputShape = inputShape;
            OutputShape = inputShape.Clone();

            _channels = inputShape[0];
            int height = inputShape[1];
            int width = inputShape[2];
            _spatialSize = height * width;
        }

        public override void Initialize(Random random)
        {
            _gamma = new Tensor(_channels);
            _beta = new Tensor(_channels);
            _movingMean = new Tensor(_channels);
            _movingVar = new Tensor(_channels);

            _gamma.Fill(1.0f);
            _beta.Fill(0.0f);
            _movingMean.Fill(0.0f);
            _movingVar.Fill(1.0f);
        }

        public override ITensor Forward(ITensor input, bool isTraining = true)
        {
            if (isTraining)
                _input = input.Clone();
            else
                _input = null;

            float[] inputData = input.Data;
            var output = new Tensor(OutputShape.Dimensions);
            float[] outputData = output.Data;

            if (isTraining)
            {
                _mean = new Tensor(_channels);
                _var = new Tensor(_channels);
                _xNorm = new Tensor(InputShape.Dimensions);
                float[] meanData = _mean.Data;
                float[] varData = _var.Data;
                float[] xNormData = _xNorm.Data;
                float[] gammaData = _gamma.Data;
                float[] betaData = _beta.Data;

                // 1. 并行计算每个通道的均值
                Parallel.For(0, _channels, c =>
                {
                    int channelOffset = c * _spatialSize;
                    Vector<float> sumVec = Vector<float>.Zero;
                    int i = 0;

                    for (; i <= _spatialSize - _simdLength; i += _simdLength)
                    {
                        int idx = channelOffset + i;
                        sumVec += new Vector<float>(inputData, idx);
                    }

                    float sum = SumVector(sumVec);
                    for (; i < _spatialSize; i++)
                        sum += inputData[channelOffset + i];

                    meanData[c] = sum / _spatialSize;
                });

                // 2. 并行计算每个通道的方差
                Parallel.For(0, _channels, c =>
                {
                    int channelOffset = c * _spatialSize;
                    float mean = meanData[c];
                    Vector<float> meanVec = new Vector<float>(mean);
                    Vector<float> sumSqVec = Vector<float>.Zero;
                    int i = 0;

                    for (; i <= _spatialSize - _simdLength; i += _simdLength)
                    {
                        int idx = channelOffset + i;
                        Vector<float> xVec = new Vector<float>(inputData, idx);
                        Vector<float> diffVec = xVec - meanVec;
                        sumSqVec += diffVec * diffVec;
                    }

                    float sumSq = SumVector(sumSqVec);
                    for (; i < _spatialSize; i++)
                    {
                        float diff = inputData[channelOffset + i] - mean;
                        sumSq += diff * diff;
                    }

                    varData[c] = sumSq / _spatialSize + _epsilon;
                });

                // 3. 并行计算归一化值和输出
                Parallel.For(0, _channels, c =>
                {
                    int channelOffset = c * _spatialSize;
                    float mean = meanData[c];
                    float var = varData[c];
                    float stdInv = 1.0f / (float)Math.Sqrt(var);
                    float gamma = gammaData[c];
                    float beta = betaData[c];

                    Vector<float> meanVec = new Vector<float>(mean);
                    Vector<float> stdInvVec = new Vector<float>(stdInv);
                    Vector<float> gammaVec = new Vector<float>(gamma);
                    Vector<float> betaVec = new Vector<float>(beta);

                    int i = 0;
                    for (; i <= _spatialSize - _simdLength; i += _simdLength)
                    {
                        int idx = channelOffset + i;
                        Vector<float> xVec = new Vector<float>(inputData, idx);
                        Vector<float> xNormVec = (xVec - meanVec) * stdInvVec;
                        Vector<float> outputVec = xNormVec * gammaVec + betaVec;

                        xNormVec.CopyTo(xNormData, idx);
                        outputVec.CopyTo(outputData, idx);
                    }

                    for (; i < _spatialSize; i++)
                    {
                        int idx = channelOffset + i;
                        float x = inputData[idx];
                        float xNorm = (x - mean) * stdInv;
                        xNormData[idx] = xNorm;
                        outputData[idx] = gamma * xNorm + beta;
                    }
                });

                // 4. 更新移动均值和方差
                float momentum = _momentum;
                float oneMinusMomentum = 1 - momentum;
                Parallel.For(0, _channels, c =>
                {
                    _movingMean.Data[c] = momentum * _movingMean.Data[c] + oneMinusMomentum * meanData[c];
                    _movingVar.Data[c] = momentum * _movingVar.Data[c] + oneMinusMomentum * varData[c];
                });
            }
            else
            {
                // 推理模式：使用移动均值和方差
                float[] gammaData = _gamma.Data;
                float[] betaData = _beta.Data;
                float[] movingMeanData = _movingMean.Data;
                float[] movingVarData = _movingVar.Data;

                Parallel.For(0, _channels, c =>
                {
                    int channelOffset = c * _spatialSize;
                    float mean = movingMeanData[c];
                    float var = movingVarData[c] + _epsilon;
                    float stdInv = 1.0f / (float)Math.Sqrt(var);
                    float gamma = gammaData[c];
                    float beta = betaData[c];

                    Vector<float> meanVec = new Vector<float>(mean);
                    Vector<float> stdInvVec = new Vector<float>(stdInv);
                    Vector<float> gammaVec = new Vector<float>(gamma);
                    Vector<float> betaVec = new Vector<float>(beta);

                    int i = 0;
                    for (; i <= _spatialSize - _simdLength; i += _simdLength)
                    {
                        int idx = channelOffset + i;
                        Vector<float> xVec = new Vector<float>(inputData, idx);
                        Vector<float> outputVec = gammaVec * ((xVec - meanVec) * stdInvVec) + betaVec;
                        outputVec.CopyTo(outputData, idx);
                    }

                    for (; i < _spatialSize; i++)
                    {
                        int idx = channelOffset + i;
                        outputData[idx] = gamma * ((inputData[idx] - mean) * stdInv) + beta;
                    }
                });
            }

            return output;
        }

        public override ITensor Backward(ITensor gradient, float learningRate)
        {
            float[] gradData = gradient.Data;
            float[] inputData = _input.Data;
            float[] xNormData = _xNorm.Data;
            float[] meanData = _mean.Data;
            float[] varData = _var.Data;
            float[] gammaData = _gamma.Data;
            float[] betaData = _beta.Data;

            var inputGradient = new Tensor(InputShape.Dimensions);
            float[] inGradData = inputGradient.Data;
            var dGamma = new Tensor(_channels);
            var dBeta = new Tensor(_channels);
            float[] dGammaData = dGamma.Data;
            float[] dBetaData = dBeta.Data;

            // 1. 并行计算dGamma和dBeta
            Parallel.For(0, _channels, c =>
            {
                int channelOffset = c * _spatialSize;
                Vector<float> sumGammaVec = Vector<float>.Zero;
                Vector<float> sumBetaVec = Vector<float>.Zero;
                int i = 0;

                for (; i <= _spatialSize - _simdLength; i += _simdLength)
                {
                    int idx = channelOffset + i;
                    Vector<float> gradVec = new Vector<float>(gradData, idx);
                    Vector<float> xNormVec = new Vector<float>(xNormData, idx);
                    sumGammaVec += gradVec * xNormVec;
                    sumBetaVec += gradVec;
                }

                float sumGamma = SumVector(sumGammaVec);
                float sumBeta = SumVector(sumBetaVec);
                for (; i < _spatialSize; i++)
                {
                    int idx = channelOffset + i;
                    sumGamma += gradData[idx] * xNormData[idx];
                    sumBeta += gradData[idx];
                }

                dGammaData[c] = sumGamma;
                dBetaData[c] = sumBeta;
            });

            // 2. 并行计算输入梯度
            Parallel.For(0, _channels, c =>
            {
                int channelOffset = c * _spatialSize;
                float std = (float)Math.Sqrt(varData[c]);
                float varInv = 1.0f / std;
                float gamma = gammaData[c];
                float mean = meanData[c];
                float dGammaC = dGammaData[c];
                float dBetaC = dBetaData[c];
                float spatialInv = 1.0f / _spatialSize;

                float gammaVarInv = gamma * varInv;
                float term2Coeff = gammaVarInv * spatialInv;
                float varInv3 = (float)Math.Pow(varInv, 3);
                float term3Coeff = varInv3 * dGammaC;

                Vector<float> gammaVarInvVec = new Vector<float>(gammaVarInv);
                Vector<float> term2CoeffVec = new Vector<float>(term2Coeff);
                Vector<float> meanVec = new Vector<float>(mean);
                Vector<float> term3CoeffVec = new Vector<float>(term3Coeff);
                Vector<float> dBetaCVec = new Vector<float>(dBetaC);

                int i = 0;
                for (; i <= _spatialSize - _simdLength; i += _simdLength)
                {
                    int idx = channelOffset + i;
                    Vector<float> gradVec = new Vector<float>(gradData, idx);
                    Vector<float> xVec = new Vector<float>(inputData, idx);

                    Vector<float> term1 = gammaVarInvVec * gradVec;
                    Vector<float> xMinusMean = xVec - meanVec;
                    Vector<float> term3 = term3CoeffVec * xMinusMean;
                    Vector<float> term2 = term2CoeffVec * (dBetaCVec + term3);

                    Vector<float> inGradVec = term1 - term2;
                    inGradVec.CopyTo(inGradData, idx);
                }

                for (; i < _spatialSize; i++)
                {
                    int idx = channelOffset + i;
                    float grad = gradData[idx];
                    float x = inputData[idx];

                    float term1 = gammaVarInv * grad;
                    float xMinusMean = x - mean;
                    float term3 = term3Coeff * xMinusMean;
                    float term2 = term2Coeff * (dBetaC + term3);

                    inGradData[idx] = term1 - term2;
                }
            });

            // 3. 在反向传播中直接更新参数（无需通过UpdateParameters）
            Parallel.For(0, _channels, c =>
            {
                gammaData[c] -= learningRate * dGammaData[c];
                betaData[c] -= learningRate * dBetaData[c];
            });

            return inputGradient;
        }

        /// <summary>
        /// 空实现：参数更新已在Backward方法中完成
        /// </summary>
        public override void UpdateParameters(IOptimizer optimizer)
        {
            // 有意留空：批归一化层的参数更新在Backward中直接完成
            // 无需通过优化器进行额外更新，保持接口一致性即可
        }

        // 辅助函数：累加SIMD向量的所有元素
        private float SumVector(Vector<float> vector)
        {
            float sum = 0;
            for (int i = 0; i < _simdLength; i++)
                sum += vector[i];
            return sum;
        }
    }
}
