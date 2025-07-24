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
    /// 优化的卷积层实现，利用SIMD指令和多线程加速计算
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

        // SIMD相关变量（不再是const，因为Vector<float>.Count不是编译时常量）
        private readonly int _vectorLength;
        private int _vectorizedKernelSize;

        public override bool HasParameters => true;

        public ConvolutionLayer(int kernelSize, int filters, int stride = 1, int padding = 1, string name = "Conv")
            : base(name)
        {
            _kernelSize = kernelSize;
            _filters = filters;
            _stride = stride;
            _padding = padding;

            // 初始化SIMD长度（在构造函数中获取，而不是作为const）
            _vectorLength = Vector<float>.Count;
            // 计算向量化的核大小（向上取整到VectorLength的倍数）
            _vectorizedKernelSize = ((kernelSize * kernelSize + _vectorLength - 1) / _vectorLength) * _vectorLength;
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
            if (isTraining)
                _input = input.Clone();
            else
                _input = null;

            int inputChannels = InputShape[0];
            int inputHeight = InputShape[1];
            int inputWidth = InputShape[2];
            int outputHeight = OutputShape[1];
            int outputWidth = OutputShape[2];

            var output = new Tensor(OutputShape.Dimensions);
            float[] outputData = output.Data;

            // 使用多线程并行处理每个滤波器
            Parallel.For(0, _filters, f =>
            {
                int filterOffset = f * outputHeight * outputWidth;
                float bias = _biases[f];

                // 处理每个输出位置
                for (int h = 0; h < outputHeight; h++)
                {
                    int heightOffset = h * outputWidth;

                    for (int w = 0; w < outputWidth; w++)
                    {
                        int outputIndex = filterOffset + heightOffset + w;
                        outputData[outputIndex] = bias + ComputeConvolutionSum(f, h, w, input,
                            inputChannels, inputHeight, inputWidth);
                    }
                }
            });

            return output;
        }

        private float ComputeConvolutionSum(int filter, int outH, int outW, ITensor input,
                                           int inputChannels, int inputHeight, int inputWidth)
        {
            float sum = 0f;
            int vectorizedCount = _vectorizedKernelSize / _vectorLength;

            // 按输入通道处理
            for (int c = 0; c < inputChannels; c++)
            {
                // 准备输入窗口和卷积核的向量化数据
                float[] kernelVectorData = new float[_vectorizedKernelSize];
                float[] inputVectorData = new float[_vectorizedKernelSize];

                int dataIndex = 0;
                for (int kh = 0; kh < _kernelSize; kh++)
                {
                    for (int kw = 0; kw < _kernelSize; kw++)
                    {
                        int inputH = outH * _stride + kh - _padding;
                        int inputW = outW * _stride + kw - _padding;

                        // 填充输入数据（边界外为0）
                        inputVectorData[dataIndex] = (inputH >= 0 && inputH < inputHeight &&
                                                    inputW >= 0 && inputW < inputWidth)
                                                    ? input[c, inputH, inputW]
                                                    : 0f;

                        // 填充卷积核数据
                        kernelVectorData[dataIndex] = _kernels[filter, c, kh, kw];
                        dataIndex++;
                    }
                }

                // 使用SIMD指令计算点积
                Vector<float> sumVector = Vector<float>.Zero;
                for (int i = 0; i < vectorizedCount; i++)
                {
                    int vectorIndex = i * _vectorLength;
                    Vector<float> vInput = new Vector<float>(inputVectorData, vectorIndex);
                    Vector<float> vKernel = new Vector<float>(kernelVectorData, vectorIndex);
                    sumVector += vInput * vKernel;
                }

                // 累加向量结果
                sum += SumVector(sumVector);
            }

            return sum;
        }

        private float SumVector(Vector<float> vector)
        {
            float sum = 0;
            for (int i = 0; i < _vectorLength; i++)
            {
                sum += vector[i];
            }
            return sum;
        }

        public override ITensor Backward(ITensor gradient, float learningRate)
        {
            int inputChannels = InputShape[0];
            int inputHeight = InputShape[1];
            int inputWidth = InputShape[2];
            int outputHeight = OutputShape[1];
            int outputWidth = OutputShape[2];

            // 初始化梯度张量
            kernelGradients = new Tensor(_filters, inputChannels, _kernelSize, _kernelSize);
            biasGradients = new Tensor(_filters);
            var inputGradient = new Tensor(InputShape.Dimensions);

            // 并行计算每个滤波器的梯度
            Parallel.For(0, _filters, f =>
            {
                ComputeBiasGradient(f, gradient, outputHeight, outputWidth);
                ComputeKernelGradients(f, inputChannels, inputHeight, inputWidth,
                                     outputHeight, outputWidth, gradient);
                ComputeInputGradients(f, inputChannels, inputHeight, inputWidth,
                                    outputHeight, outputWidth, gradient, inputGradient);
            });

            return inputGradient;
        }

        private void ComputeBiasGradient(int filter, ITensor gradient, int outputHeight, int outputWidth)
        {
            float sum = 0f;
            float[] gradData = gradient.Data;
            int filterOffset = filter * outputHeight * outputWidth;

            // 使用SIMD加速偏置梯度计算
            int vectorCount = (outputHeight * outputWidth) / _vectorLength;
            int remaining = (outputHeight * outputWidth) % _vectorLength;

            Vector<float> sumVector = Vector<float>.Zero;
            for (int i = 0; i < vectorCount; i++)
            {
                int index = filterOffset + i * _vectorLength;
                sumVector += new Vector<float>(gradData, index);
            }

            sum = SumVector(sumVector);

            // 处理剩余元素
            for (int i = 0; i < remaining; i++)
            {
                int index = filterOffset + vectorCount * _vectorLength + i;
                sum += gradData[index];
            }

            biasGradients[filter] = sum;
        }

        private void ComputeKernelGradients(int filter, int inputChannels, int inputHeight, int inputWidth,
                                          int outputHeight, int outputWidth, ITensor gradient)
        {
            float[] gradData = gradient.Data;
            int filterOffset = filter * outputHeight * outputWidth;

            for (int c = 0; c < inputChannels; c++)
            {
                for (int kh = 0; kh < _kernelSize; kh++)
                {
                    for (int kw = 0; kw < _kernelSize; kw++)
                    {
                        float sum = 0f;

                        // 向量化计算卷积核梯度
                        for (int h = 0; h < outputHeight; h++)
                        {
                            int heightOffset = h * outputWidth;
                            int vectorCount = outputWidth / _vectorLength;
                            int remaining = outputWidth % _vectorLength;

                            Vector<float> sumVector = Vector<float>.Zero;
                            for (int v = 0; v < vectorCount; v++)
                            {
                                int w = v * _vectorLength;
                                int gradIndex = filterOffset + heightOffset + w;

                                // 收集输入值
                                float[] inputVals = new float[_vectorLength];
                                for (int i = 0; i < _vectorLength; i++)
                                {
                                    int currentW = w + i;
                                    int inputH = h * _stride + kh - _padding;
                                    int inputW = currentW * _stride + kw - _padding;

                                    inputVals[i] = (inputH >= 0 && inputH < inputHeight &&
                                                  inputW >= 0 && inputW < inputWidth)
                                                  ? _input[c, inputH, inputW]
                                                  : 0f;
                                }

                                // SIMD计算
                                Vector<float> vInput = new Vector<float>(inputVals);
                                Vector<float> vGrad = new Vector<float>(gradData, gradIndex);
                                sumVector += vInput * vGrad;
                            }

                            sum += SumVector(sumVector);

                            // 处理剩余元素
                            for (int w = vectorCount * _vectorLength; w < outputWidth; w++)
                            {
                                int gradIndex = filterOffset + heightOffset + w;
                                int inputH = h * _stride + kh - _padding;
                                int inputW = w * _stride + kw - _padding;

                                if (inputH >= 0 && inputH < inputHeight &&
                                    inputW >= 0 && inputW < inputWidth)
                                {
                                    sum += _input[c, inputH, inputW] * gradData[gradIndex];
                                }
                            }
                        }

                        kernelGradients[filter, c, kh, kw] = sum;
                    }
                }
            }
        }

        // 修改方法签名，添加inputGradient参数
        private void ComputeInputGradients(int filter, int inputChannels, int inputHeight, int inputWidth,
                                         int outputHeight, int outputWidth, ITensor gradient, Tensor inputGradient)
        {
            float[] gradData = gradient.Data;
            int filterOffset = filter * outputHeight * outputWidth;

            for (int c = 0; c < inputChannels; c++)
            {
                // 并行处理输入高度
                Parallel.For(0, inputHeight, h =>
                {
                    for (int w = 0; w < inputWidth; w++)
                    {
                        float sum = 0f;

                        // 使用SIMD加速输入梯度计算
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
                                    int gradIndex = filterOffset + outputH * outputWidth + outputW;
                                    sum += _kernels[filter, c, kh, kw] * gradData[gradIndex];
                                }
                            }
                        }

                        lock (inputGradient) // 现在inputGradient在当前上下文中存在
                        {
                            inputGradient[c, h, w] += sum;
                        }
                    }
                });
            }
        }

        public override void UpdateParameters(IOptimizer optimizer)
        {
            optimizer.UpdateParameter(_kernels, kernelGradients);
            optimizer.UpdateParameter(_biases, biasGradients);
        }

        /// <summary>
        /// 序列化卷积层参数
        /// </summary>
        public override JsonArray GetParameters()
        {
            JsonArray parameters = new JsonArray();

            // 1. 层配置信息（超参数）
            JsonObject layerConfig = new JsonObject
            {
                ["type"] = "ConvolutionLayer",
                ["name"] = Name,
                ["kernelSize"] = _kernelSize,
                ["filters"] = _filters,
                ["stride"] = _stride,
                ["padding"] = _padding,
                ["inputChannels"] = InputShape[0],
                ["kernelShape"] = new JsonArray(_filters, InputShape[0], _kernelSize, _kernelSize)
            };
            parameters.Add(layerConfig);

            // 2. 卷积核参数
            parameters.Add(JsonArrayHelper.FromFloatArray(_kernels.Data));

            // 3. 偏置参数
            parameters.Add(JsonArrayHelper.FromFloatArray(_biases.Data));

            return parameters;
        }

        /// <summary>
        /// 反序列化卷积层参数
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
                    layerConfig["type"]?.ToString() != "ConvolutionLayer" ||
                    layerConfig["name"]?.ToString() != Name)
                    return false;

                // 验证关键超参数是否匹配
                if (layerConfig["kernelSize"]?.GetValue<int>() != _kernelSize ||
                    layerConfig["filters"]?.GetValue<int>() != _filters ||
                    layerConfig["stride"]?.GetValue<int>() != _stride ||
                    layerConfig["padding"]?.GetValue<int>() != _padding)
                    return false;

                // 验证输入通道数是否匹配
                int savedInputChannels = layerConfig["inputChannels"]?.GetValue<int>() ?? -1;
                if (savedInputChannels != InputShape[0])
                    return false;

                // 2. 加载卷积核参数
                JsonArray kernelArray = param[1] as JsonArray;
                float[] kernelData = JsonArrayHelper.ToFloatArray(kernelArray);

                int expectedKernelSize = _filters * InputShape[0] * _kernelSize * _kernelSize;
                if (kernelData == null || kernelData.Length != expectedKernelSize)
                    return false;

                _kernels.CopyFrom(kernelData);

                // 3. 加载偏置参数
                JsonArray biasArray = param[2] as JsonArray;
                float[] biasData = JsonArrayHelper.ToFloatArray(biasArray);

                if (biasData == null || biasData.Length != _filters)
                    return false;

                _biases.CopyFrom(biasData);

                return true;
            }
            catch
            {
                return false; // 任何解析错误都返回失败
            }
        }
    }

//[
//{
//    "type": "ConvolutionLayer",
//    "name": "Conv",
//    "kernelSize": 3,
//    "filters": 64,
//    "stride": 1,
//    "padding": 1,
//    "inputChannels": 3,
//    "kernelShape": [64, 3, 3, 3]
//},
//  [0.12, 0.34, ..., 0.56],  // 卷积核参数数组（长度为filters×inputChannels×kernelSize×kernelSize）
//  [0.01, 0.02, ..., 0.064]  // 偏置参数数组（长度为filters）
//]

}
