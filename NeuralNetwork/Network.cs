using System;
using System.Collections.Generic;
using NeuralNetwork.Layers;

namespace NeuralNetwork
{
    /// <summary>
    /// 神经网络类，用于组合各种层并执行训练和预测
    /// </summary>
    public class Network
    {
        private readonly List<ILayer> _layers = new List<ILayer>();
        private readonly ILossFunction _lossFunction;
        private readonly float _learningRate;

        /// <summary>
        /// 初始化神经网络
        /// </summary>
        /// <param name="lossType">损失函数类型</param>
        /// <param name="learningRate">学习率</param>
        public Network(LossType lossType, float learningRate = 0.01f)
        {
            _learningRate = learningRate;
            
            // 根据损失函数类型创建相应的损失函数实例
            _lossFunction = lossType switch
            {
                LossType.MeanSquaredError => new MeanSquaredErrorLoss(),
                LossType.CrossEntropy => new CrossEntropyLoss(),
                _ => throw new ArgumentException("Invalid loss function type")
            };
        }

        /// <summary>
        /// 向网络添加层
        /// </summary>
        /// <param name="layer">要添加的层</param>
        public void AddLayer(ILayer layer)
        {
            _layers.Add(layer);
        }

        /// <summary>
        /// 前向传播，计算网络输出
        /// </summary>
        /// <param name="input">输入数据</param>
        /// <returns>网络输出</returns>
        public float[,,] Predict(float[,,] input)
        {
            float[,,] output = input;
            foreach (var layer in _layers)
            {
                output = layer.Forward(output);
            }
            return output;
        }

        /// <summary>
        /// 训练一步
        /// </summary>
        /// <param name="input">输入数据</param>
        /// <param name="target">目标输出</param>
        /// <returns>损失值</returns>
        public float TrainStep(float[,,] input, float[,,] target)
        {
            // 前向传播
            float[,,] output = Predict(input);
            
            // 计算损失
            float loss = _lossFunction.CalculateLoss(output, target);
            
            // 计算损失梯度
            float[,,] gradient = _lossFunction.CalculateGradient(output, target);
            
            // 反向传播
            for (int i = _layers.Count - 1; i >= 0; i--)
            {
                gradient = _layers[i].Backward(gradient, _learningRate);
            }
            
            return loss;
        }

        /// <summary>
        /// 训练网络
        /// </summary>
        /// <param name="inputs">输入数据批次</param>
        /// <param name="targets">目标输出批次</param>
        /// <param name="epochs">训练轮数</param>
        /// <param name="batchSize">批次大小</param>
        public void Train(List<float[,,]> inputs, List<float[,,]> targets, int epochs, int batchSize = 32)
        {
            if (inputs.Count != targets.Count)
            {
                throw new ArgumentException("Inputs and targets must have the same count");
            }

            int dataCount = inputs.Count;
            
            for (int epoch = 0; epoch < epochs; epoch++)
            {
                float totalLoss = 0;
                int correctPredictions = 0;

                // 随机打乱数据顺序
                var indices = new List<int>();
                for (int i = 0; i < dataCount; i++) indices.Add(i);
                indices.Shuffle();

                // 按批次训练
                for (int i = 0; i < dataCount; i += batchSize)
                {
                    float batchLoss = 0;
                    
                    // 处理批次中的每个样本
                    for (int j = i; j < Math.Min(i + batchSize, dataCount); j++)
                    {
                        int idx = indices[j];
                        float loss = TrainStep(inputs[idx], targets[idx]);
                        batchLoss += loss;
                        
                        // 计算准确率（假设是分类任务）
                        if (IsPredictionCorrect(inputs[idx], targets[idx]))
                        {
                            correctPredictions++;
                        }
                    }
                    
                    totalLoss += batchLoss;
                }
                
                // 计算平均损失和准确率
                float averageLoss = totalLoss / dataCount;
                float accuracy = (float)correctPredictions / dataCount;
                
                // 打印训练进度
                Console.WriteLine($"Epoch {epoch + 1}/{epochs} - Loss: {averageLoss:F6} - Accuracy: {accuracy:F4}");
            }
        }

        /// <summary>
        /// 检查预测是否正确（适用于分类任务）
        /// </summary>
        private bool IsPredictionCorrect(float[,,] input, float[,,] target)
        {
            float[,,] output = Predict(input);
            
            // 找到预测和目标中的最大值索引
            int predictedClass = 0;
            float maxOutput = output[0, 0, 0];
            for (int i = 1; i < output.GetLength(0); i++)
            {
                if (output[i, 0, 0] > maxOutput)
                {
                    maxOutput = output[i, 0, 0];
                    predictedClass = i;
                }
            }
            
            int targetClass = 0;
            float maxTarget = target[0, 0, 0];
            for (int i = 1; i < target.GetLength(0); i++)
            {
                if (target[i, 0, 0] > maxTarget)
                {
                    maxTarget = target[i, 0, 0];
                    targetClass = i;
                }
            }
            
            return predictedClass == targetClass;
        }

        /// <summary>
        /// 获取网络中的总参数数量
        /// </summary>
        public int TotalParameters
        {
            get
            {
                int count = 0;
                foreach (var layer in _layers)
                {
                    count += layer.ParameterCount;
                }
                return count;
            }
        }
    }

    /// <summary>
    /// 扩展方法类
    /// </summary>
    public static class Extensions
    {
        private static readonly Random _random = new Random();
        
        /// <summary>
        /// 随机打乱列表顺序
        /// </summary>
        public static void Shuffle<T>(this IList<T> list)
        {
            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = _random.Next(n + 1);
                T value = list[k];
                list[k] = list[n];
                list[n] = value;
            }
        }
    }
}
