using NeuralNet.Common;
using NeuralNetworkLibrary.Core;
using NeuralNetworkLibrary.Optimizers;
using System;
using System.Collections.Concurrent;
using System.Numerics;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace NeuralNetworkLibrary.Layers
{
    /// <summary>
    /// 优化的全连接层（密集层），利用SIMD和多线程最大化CPU性能
    /// </summary>
    public class DenseLayer : BaseLayer
    {
        private int _units;
        private ITensor _weights;  // [units, inputSize]
        private ITensor _biases;   // [units]
        private ITensor _input;    // 缓存输入用于反向传播
        private int _inputSize;    // 输入特征数量
        private readonly int _simdLength;  // SIMD向量长度（如AVX2为8，AVX-512为16）

        private Tensor _weightGradients;
        private Tensor _biasGradients;
        public override bool HasParameters => true;
        public override string LayerType => "DenseLayer";

        public DenseLayer(int units, string name = "Dense") : base(name)
        {
            _units = units;
            _simdLength = Vector<float>.Count;  // 运行时确定SIMD长度（依赖CPU支持）
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
            // 初始化权重和偏置（内存对齐，提升SIMD效率）
            _weights = new Tensor(_units, _inputSize);
            _biases = new Tensor(_units);

            // 使用Xavier初始化（适应ReLU等激活函数）
            float scale = (float)Math.Sqrt(2.0 / _inputSize);
            _weights.Randomize(-scale, scale, random);
            _biases.Fill(0f);
        }

        /// <summary>
        /// 前向传播：y = W·x + b（利用SIMD和多线程加速）
        /// </summary>
        public override ITensor Forward(ITensor input, bool isTraining = true)
        {
            // 训练时缓存输入（深拷贝）
            _input = isTraining ? input.Clone() : null;

            var output = new Tensor(_units);
            float[] outputData = output.Data;
            float[] weightsData = _weights.Data;
            float[] biasesData = _biases.Data;
            float[] inputData = input.Data;

            // 多线程并行计算每个输出单元（按单元粒度拆分，平衡负载）
            int batchSize = Math.Max(1, Environment.ProcessorCount * 2);  // 线程数为CPU核心数的2倍
            Parallel.ForEach(
                Partitioner.Create(0, _units),
                new ParallelOptions { MaxDegreeOfParallelism = batchSize },
                range =>
                {
                    for (int i = range.Item1; i < range.Item2; i++)
                    {
                        // 计算 W[i]·x（使用SIMD加速点积）
                        float sum = SimdDotProduct(
                            weightsData, i * _inputSize,  // 权重起始地址：第i行的权重
                            inputData, 0,                // 输入向量起始地址
                            _inputSize                   // 向量长度
                        );
                        // 加上偏置 b[i]
                        outputData[i] = sum + biasesData[i];
                    }
                }
            );

            return output;
        }

        /// <summary>
        /// 反向传播：计算梯度并更新参数
        /// </summary>
        public override ITensor Backward(ITensor gradient, float learningRate)
        {
            float[] gradData = gradient.Data;
            float[] inputData = _input.Data;
            int inputSize = _inputSize;
            int units = _units;

            // 初始化梯度张量（内存对齐）
            var weightGradients = new Tensor(units, inputSize);
            var biasGradients = new Tensor(units);
            var inputGradient = new Tensor(InputShape.Dimensions);
            float[] wGradData = weightGradients.Data;
            float[] bGradData = biasGradients.Data;
            float[] inGradData = inputGradient.Data;

            // 1. 计算偏置梯度（bGrad[i] = ∇L/∇y[i]）
            // 直接复制梯度（SIMD加速内存拷贝）
            SimdCopy(gradData, 0, bGradData, 0, units);

            // 2. 计算权重梯度（wGrad[i][j] = ∇L/∇y[i] * x[j]）
            // 多线程并行计算每个权重的梯度
            Parallel.ForEach(
                Partitioner.Create(0, units),
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount * 2 },
                range =>
                {
                    for (int i = range.Item1; i < range.Item2; i++)
                    {
                        float g = gradData[i];  // 当前输出单元的梯度
                        int weightRowStart = i * inputSize;  // 第i行权重的起始索引

                        // 计算 g * x[j]（使用SIMD加速标量×向量）
                        SimdMultiplyScalar(
                            inputData, 0,          // 输入向量x
                            g,                     // 标量g
                            wGradData, weightRowStart,  // 输出：权重梯度第i行
                            inputSize              // 向量长度
                        );
                    }
                }
            );

            // 3. 计算输入梯度（inGrad[j] = sum(∇L/∇y[i] * W[i][j])）
            // 多线程并行计算每个输入特征的梯度
            Parallel.ForEach(
                Partitioner.Create(0, inputSize),
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount * 2 },
                range =>
                {
                    for (int j = range.Item1; j < range.Item2; j++)
                    {
                        float sum = 0f;
                        // 累加所有输出单元的梯度×权重
                        for (int i = 0; i < units; i++)
                        {
                            sum += gradData[i] * _weights[i, j];
                        }
                        inGradData[j] = sum;
                    }
                }
            );

            // 保存梯度用于参数更新
            _weightGradients = weightGradients;
            _biasGradients = biasGradients;

            return inputGradient;
        }

        public override void UpdateParameters(IOptimizer optimizer)
        {
            optimizer.UpdateParameter(_weights, _weightGradients);
            optimizer.UpdateParameter(_biases, _biasGradients);
        }

        // ------------------------------
        // SIMD工具函数（核心优化点）
        // ------------------------------

        /// <summary>
        /// SIMD加速的点积计算：a·b = sum(a[i] * b[i])
        /// </summary>
        private float SimdDotProduct(float[] a, int aStart, float[] b, int bStart, int length)
        {
            Vector<float> sumVec = Vector<float>.Zero;
            int i = 0;

            // 按SIMD向量长度处理（每次处理_simdLength个元素）
            for (; i <= length - _simdLength; i += _simdLength)
            {
                Vector<float> va = new Vector<float>(a, aStart + i);
                Vector<float> vb = new Vector<float>(b, bStart + i);
                sumVec += va * vb;  // SIMD乘法+累加
            }

            // 处理剩余元素（不足_simdLength的部分）
            float sum = SumVector(sumVec);
            for (; i < length; i++)
            {
                sum += a[aStart + i] * b[bStart + i];
            }

            return sum;
        }

        /// <summary>
        /// SIMD加速的标量×向量乘法：result[i] = scalar * a[i]
        /// </summary>
        private void SimdMultiplyScalar(float[] a, int aStart, float scalar, float[] result, int resultStart, int length)
        {
            Vector<float> scalarVec = new Vector<float>(scalar);  // 标量广播为向量
            int i = 0;

            // 按SIMD向量长度处理
            for (; i <= length - _simdLength; i += _simdLength)
            {
                Vector<float> va = new Vector<float>(a, aStart + i);
                Vector<float> vResult = va * scalarVec;  // SIMD乘法
                vResult.CopyTo(result, resultStart + i);  // 结果写入内存
            }

            // 处理剩余元素
            for (; i < length; i++)
            {
                result[resultStart + i] = a[aStart + i] * scalar;
            }
        }

        /// <summary>
        /// SIMD加速的内存拷贝
        /// </summary>
        private void SimdCopy(float[] source, int sourceStart, float[] dest, int destStart, int length)
        {
            int i = 0;
            for (; i <= length - _simdLength; i += _simdLength)
            {
                Vector<float> v = new Vector<float>(source, sourceStart + i);
                v.CopyTo(dest, destStart + i);
            }
            // 处理剩余元素
            for (; i < length; i++)
            {
                dest[destStart + i] = source[sourceStart + i];
            }
        }

        /// <summary>
        /// 累加SIMD向量的所有元素
        /// </summary>
        private float SumVector(Vector<float> vector)
        {
            float sum = 0;
            for (int i = 0; i < _simdLength; i++)
            {
                sum += vector[i];
            }
            return sum;
        }

        /// <summary>
        /// 序列化全连接层参数
        /// </summary>
        public override JsonArray GetParameters()
        {
            JsonArray parameters = new JsonArray();

            // 1. 层配置信息（超参数）
            JsonObject layerConfig = new JsonObject
            {
                ["type"] = "DenseLayer",
                ["name"] = Name,
                ["units"] = _units,
                ["inputSize"] = _inputSize,
                ["weightShape"] = new JsonArray(_units, _inputSize),
                ["biasShape"] = new JsonArray(_units)
            };
            parameters.Add(layerConfig);

            // 2. 权重参数（[units, inputSize]）
            parameters.Add(JsonArrayHelper.FromFloatArray(_weights.Data));

            // 3. 偏置参数（[units]）
            parameters.Add(JsonArrayHelper.FromFloatArray(_biases.Data));

            return parameters;
        }

        /// <summary>
        /// 反序列化全连接层参数
        /// </summary>
        public override bool LoadParameters(JsonArray param)
        {
            try
            {
                // 验证参数结构完整性
                if (param.Count != 3)
                    return false;

                // 1. 验证层配置信息
                JsonObject layerConfig = param[0] as JsonObject;
                if (layerConfig == null ||
                    layerConfig["type"]?.ToString() != "DenseLayer" ||
                    layerConfig["name"]?.ToString() != Name)
                    return false;

                // 验证关键超参数是否匹配
                int savedUnits = layerConfig["units"]?.GetValue<int>() ?? -1;
                int savedInputSize = layerConfig["inputSize"]?.GetValue<int>() ?? -1;
                if (savedUnits != _units || savedInputSize != _inputSize)
                    return false;

                // 2. 加载权重参数
                JsonArray weightArray = param[1] as JsonArray;
                float[] weightData = JsonArrayHelper.ToFloatArray(weightArray);

                int expectedWeightSize = _units * _inputSize;
                if (weightData == null || weightData.Length != expectedWeightSize)
                    return false;

                _weights.CopyFrom(weightData);

                // 3. 加载偏置参数
                JsonArray biasArray = param[2] as JsonArray;
                float[] biasData = JsonArrayHelper.ToFloatArray(biasArray);

                if (biasData == null || biasData.Length != _units)
                    return false;

                _biases.CopyFrom(biasData);

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
//{
//    "type": "DenseLayer",
//    "name": "Dense",
//    "units": 128,
//    "inputSize": 784,
//    "weightShape": [128, 784],
//    "biasShape": [128]
//},
//  [0.12, 0.34, ..., 0.56],  // 权重参数数组（长度为units×inputSize）
//  [0.01, 0.02, ..., 0.128]  // 偏置参数数组（长度为units）
//]

}