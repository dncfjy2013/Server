using NeuralNetworkLibrary.Core;
using NeuralNetworkLibrary.Layers;
using NeuralNetworkLibrary.LossFunctions;
using NeuralNetworkLibrary.Optimizers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace NeuralNetworkLibrary
{
    /// <summary>
    /// 神经网络主类，用于构建、训练和推理
    /// </summary>
    public class NeuralNetwork
    {
        public List<ILayer> Layers { get; }
        public ILossFunction LossFunction { get; }
        public IOptimizer Optimizer { get; }
        public TensorShape InputShape { get; private set; }
        public TensorShape OutputShape => Layers.Count > 0 ? Layers.Last().OutputShape : null;
        
        private Random _random;

        public NeuralNetwork(ILossFunction lossFunction, IOptimizer optimizer)
        {
            Layers = new List<ILayer>();
            LossFunction = lossFunction;
            Optimizer = optimizer;
            _random = new Random();
        }

        /// <summary>
        /// 向网络添加层
        /// </summary>
        public void AddLayer(ILayer layer)
        {
            Layers.Add(layer);
        }

        /// <summary>
        /// 构建网络，自动推断所有层的形状
        /// </summary>
        public void Build(int inputChannels, int inputHeight, int inputWidth)
        {
            InputShape = new TensorShape(inputChannels, inputHeight, inputWidth);
            
            // 自动推断各层的输入输出形状
            TensorShape currentShape = InputShape;
            foreach (var layer in Layers)
            {
                layer.SetInputShape(currentShape);
                currentShape = layer.OutputShape;
            }
            
            // 初始化所有层
            foreach (var layer in Layers)
            {
                layer.Initialize(_random);
            }
            
            // 打印网络结构
            PrintNetworkStructure();
        }

        /// <summary>
        /// 打印网络结构信息
        /// </summary>
        private void PrintNetworkStructure()
        {
            Console.WriteLine("网络结构:");
            Console.WriteLine($"输入形状: {InputShape}");
            
            foreach (var layer in Layers)
            {
                Console.WriteLine($"{layer.Name}: {layer.InputShape} -> {layer.OutputShape}");
            }
            
            Console.WriteLine($"输出形状: {OutputShape}");
        }

        /// <summary>
        /// 前向传播
        /// </summary>
        public ITensor Forward(ITensor input, bool isTraining = true)
        {
            ITensor current = input;
            foreach (var layer in Layers)
            {
                current = layer.Forward(current, isTraining);
            }
            return current;
        }

        /// <summary>
        /// 反向传播
        /// </summary>
        private float Backward(ITensor predicted, ITensor target)
        {
            // 计算损失
            float loss = LossFunction.Calculate(predicted, target);
            
            // 从损失函数开始反向传播
            ITensor gradient = LossFunction.CalculateGradient(predicted, target);
            
            // 应用梯度裁剪防止爆炸
            gradient = Optimizer.ApplyGradientClipping(gradient);
            
            // 反向传播通过各层
            for (int i = Layers.Count - 1; i >= 0; i--)
            {
                gradient = Layers[i].Backward(gradient, Optimizer.LearningRate);

                if (Layers[i].HasParameters)
                    Layers[i].UpdateParameters(optimizer: Optimizer);
            }

            return loss;
        }

        private JsonArray _bestParameters;  // 用于缓存最佳参数
        /// <summary>
        /// 训练网络
        /// </summary>
        public void Train(List<ITensor> inputs, List<ITensor> targets, int epochs, int batchSize)
        {
            if (inputs.Count != targets.Count)
                throw new ArgumentException("Inputs and targets must have the same count");
                
            if (Layers.Count == 0)
                throw new InvalidOperationException("Network has no layers. Add layers before training.");
                
            int numBatches = (int)Math.Ceiling((double)inputs.Count / batchSize);

            float _accuracyBase = 0.0f; // 用于记录每个epoch的精度基准

            for (int epoch = 0; epoch < epochs; epoch++)
            {
                float totalLoss = 0;
                int totalCorrect = 0;
                int totalSamples = 0;

                // 打乱数据顺序
                var shuffledIndices = Enumerable.Range(0, inputs.Count).OrderBy(x => _random.Next()).ToList();
                
                for (int b = 0; b < numBatches; b++)
                {
                    // 获取当前批次的索引
                    var batchIndices = shuffledIndices.Skip(b * batchSize).Take(batchSize).ToList();
                    float batchLoss = 0;
                    int batchCorrect = 0;
                    int batchSamples = batchIndices.Count;

                    // 处理批次中的每个样本
                    foreach (int i in batchIndices)
                    {
                        // 前向传播
                        ITensor predicted = Forward(inputs[i], isTraining: true);
                        
                        // 反向传播并更新参数
                        batchLoss += Backward(predicted, targets[i]);

                        // 计算当前样本是否预测正确
                        if (IsPredictionCorrect(predicted, targets[i]))
                        {
                            batchCorrect++;
                            totalCorrect++;
                        }
                        totalSamples++;
                    }
                    
                    // 计算批次平均损失
                    batchLoss /= batchIndices.Count;
                    totalLoss += batchLoss;
                    float batchAccuracy = (float)batchCorrect / batchSamples * 100;

                    Console.WriteLine($"{DateTime.Now} Epoch {epoch + 1}/{epochs}, Batch {b + 1}/{numBatches}, Loss: {batchLoss:F6}" +
                        $", Accuracy: {batchAccuracy:F2}%");
                }
                
                // 计算 epoch 平均损失
                float epochLoss = totalLoss / numBatches;
                float epochAccuracy = (float)totalCorrect / totalSamples * 100;

                if(epochAccuracy > _accuracyBase)
                {
                    _accuracyBase = epochAccuracy; // 更新精度基准
                    _bestParameters = new JsonArray(Layers.Select(layer => layer.GetParameters()).ToArray());
                }
                
                Console.WriteLine($"{DateTime.Now} Epoch {epoch + 1}/{epochs} 平均损失: {epochLoss:F6} 当前精度: {epochAccuracy:F2}%  基准精度: {_accuracyBase:F2}%");
                
            }
        }

        /// <summary>
        /// 预测（推理）
        /// </summary>
        public ITensor Predict(ITensor input)
        {
            if (_bestParameters == null)
            {
                throw new InvalidOperationException("请先训练模型以获取最佳参数");
            }

            // 加载最佳参数到各层
            for (int i = 0; i < Layers.Count; i++)
            {
                if (!Layers[i].LoadParameters(_bestParameters[i] as JsonArray))
                {
                    throw new InvalidOperationException($"加载第{i}层参数失败");
                }
            }

            return Forward(input, isTraining: false);
        }

        /// <summary>
        /// 判断预测结果是否正确
        /// 适用于分类问题，通过比较最大值索引判断
        /// </summary>
        private bool IsPredictionCorrect(ITensor predicted, ITensor target)
        {
            // 对于分类问题，假设预测和目标都是one-hot编码或概率分布
            // 找到预测值中概率最高的索引
            int predictedClass = GetMaxValueIndex(predicted);

            // 找到目标值中标签位置（通常是1的位置）
            int targetClass = GetMaxValueIndex(target);

            return predictedClass == targetClass;
        }

        /// <summary>
        /// 获取张量中最大值的索引
        /// </summary>
        private int GetMaxValueIndex(ITensor tensor)
        {
            float maxValue = float.MinValue;
            int maxIndex = 0;

            // 假设是1D张量（分类输出）
            for (int i = 0; i < tensor.Shape[0]; i++)
            {
                float value = tensor[i];
                if (value > maxValue)
                {
                    maxValue = value;
                    maxIndex = i;
                }
            }

            return maxIndex;
        }

    }
}
