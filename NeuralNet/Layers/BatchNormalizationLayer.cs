using NeuralNet.Common;
using NeuralNetworkLibrary.Core;
using NeuralNetworkLibrary.Optimizers;
using System;
using System.Numerics;
using System.Text.Json.Nodes;
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

        public override string LayerType => "BatchNormalizationLayer";
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

        // 辅助函数：累加SIMD向量的所有元素
        private float SumVector(Vector<float> vector)
        {
            float sum = 0;
            for (int i = 0; i < _simdLength; i++)
                sum += vector[i];
            return sum;
        }

        public override void UpdateParameters(IOptimizer optimizer)
        {

        }

        /// <summary>
        /// 序列化批归一化层参数
        /// </summary>
        public override JsonArray GetParameters()
        {
            JsonArray parameters = new JsonArray();

            // 1. 层基本信息
            JsonObject layerInfo = new JsonObject
            {
                ["type"] = "BatchNormalizationLayer",
                ["name"] = Name,
                ["momentum"] = _momentum,
                ["channels"] = _channels
            };
            parameters.Add(layerInfo);

            // 2. 可训练参数（gamma和beta）
            JsonObject trainableParams = new JsonObject
            {
                ["gamma"] = JsonArrayHelper.FromFloatArray(_gamma.Data),
                ["beta"] = JsonArrayHelper.FromFloatArray(_beta.Data)
            };
            parameters.Add(trainableParams);

            // 3. 移动统计量（推理时使用）
            JsonObject movingStats = new JsonObject
            {
                ["movingMean"] = JsonArrayHelper.FromFloatArray(_movingMean.Data),
                ["movingVar"] = JsonArrayHelper.FromFloatArray(_movingVar.Data)
            };
            parameters.Add(movingStats);

            return parameters;
        }

        /// <summary>
        /// 反序列化批归一化层参数
        /// </summary>
        public override bool LoadParameters(JsonArray param)
        {
            try
            {
                // 验证参数结构完整性
                if (param.Count != 3)
                    return false;

                // 1. 验证层信息
                JsonObject layerInfo = param[0] as JsonObject;
                if (layerInfo == null ||
                    layerInfo["type"]?.ToString() != "BatchNormalizationLayer" ||
                    layerInfo["name"]?.ToString() != Name)
                    return false;

                int savedChannels = layerInfo["channels"]?.GetValue<int>() ?? -1;
                if (savedChannels != _channels)
                    return false; // 通道数不匹配

                // 2. 加载可训练参数（gamma和beta）
                JsonObject trainableParams = param[1] as JsonObject;
                if (trainableParams == null)
                    return false;

                float[] gammaData = JsonArrayHelper.ToFloatArray(trainableParams["gamma"] as JsonArray);
                float[] betaData = JsonArrayHelper.ToFloatArray(trainableParams["beta"] as JsonArray);

                if (gammaData == null || betaData == null ||
                    gammaData.Length != _channels || betaData.Length != _channels)
                    return false;

                _gamma.CopyFrom(gammaData);
                _beta.CopyFrom(betaData);

                // 3. 加载移动统计量
                JsonObject movingStats = param[2] as JsonObject;
                if (movingStats == null)
                    return false;

                float[] movingMeanData = JsonArrayHelper.ToFloatArray(movingStats["movingMean"] as JsonArray);
                float[] movingVarData = JsonArrayHelper.ToFloatArray(movingStats["movingVar"] as JsonArray);

                if (movingMeanData == null || movingVarData == null ||
                    movingMeanData.Length != _channels || movingVarData.Length != _channels)
                    return false;

                _movingMean.CopyFrom(movingMeanData);
                _movingVar.CopyFrom(movingVarData);

                return true;
            }
            catch
            {
                return false; // 任何解析错误都返回失败
            }
        }

        public override void ResetParameters(Random random)
        {
            Initialize(random);
        }
    }

    //[
    //  {
    //    "type": "BatchNormalizationLayer",
    //    "name": "BN",
    //    "momentum": 0.9,
    //    "channels": 64
    //  },
    //  {
    //    "gamma": [1.0, 1.0, ..., 1.0],  // 长度为channels的数组
    //    "beta": [0.0, 0.0, ..., 0.0]     // 长度为channels的数组
    //  },
    //  {
    //    "movingMean": [0.12, 0.34, ..., 0.56],  // 长度为channels的数组
    //    "movingVar": [0.89, 0.76, ..., 0.45]     // 长度为channels的数组
    //  }
    //]
}
