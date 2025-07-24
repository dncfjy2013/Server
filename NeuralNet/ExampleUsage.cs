using System;
using System.Collections.Generic;
using NeuralNetworkLibrary;
using NeuralNetworkLibrary.Activations;
using NeuralNetworkLibrary.Core;
using NeuralNetworkLibrary.Layers;
using NeuralNetworkLibrary.LossFunctions;
using NeuralNetworkLibrary.Optimizers;

namespace NeuralNetworkExample
{
    class NExample
    {
        public static void Main()
        {
            // 创建一个包含卷积层、全连接层和Dropout的神经网络
            Console.WriteLine("创建混合神经网络...");
            
            // 定义损失函数和优化器
            // 使用Adam优化器，并设置梯度裁剪阈值防止梯度爆炸
            var optimizer = new AdamOptimizer(
                learningRate: 0.0001f, 
                gradientClipThreshold: 1.0f  // 梯度裁剪阈值
            );
            
            // 对于分类问题，使用交叉熵损失
            var lossFunction = new CrossEntropyLoss();
            
            // 创建神经网络
            var network = new NeuralNetwork(lossFunction, optimizer);
            
            // 添加网络层
            // 卷积部分
            network.AddLayer(new ConvolutionLayer(kernelSize: 3, filters: 32, stride: 1, padding: 1, name: "Conv1"));
            network.AddLayer(new BatchNormalizationLayer(name: "BN1"));
            network.AddLayer(new ActivationLayer(new ReLUActivation(), name: "ReLU1"));
            network.AddLayer(new PoolingLayer(poolSize: 2, name: "Pool1"));
            
            network.AddLayer(new ConvolutionLayer(kernelSize: 3, filters: 64, stride: 1, padding: 1, name: "Conv2"));
            network.AddLayer(new BatchNormalizationLayer(name: "BN2"));
            network.AddLayer(new ActivationLayer(new ReLUActivation(), name: "ReLU2"));
            network.AddLayer(new PoolingLayer(poolSize: 2, name: "Pool2"));

            // 添加Dropout防止过拟合
            network.AddLayer(new DropoutLayer(dropoutRate: 0.5f, name: "Dropout1"));

            // 展平以便连接全连接层
            network.AddLayer(new FlattenLayer(name: "Flatten"));
            
            // 全连接部分
            network.AddLayer(new DenseLayer(units: 128, name: "Dense1"));
            network.AddLayer(new ActivationLayer(new ReLUActivation(), name: "ReLU3"));

            // 再添加一个Dropout
            network.AddLayer(new DropoutLayer(dropoutRate: 0.5f, name: "Dropout2"));

            // 输出层（假设有10个类别）
            network.AddLayer(new DenseLayer(units: 10, name: "Output"));
            network.AddLayer(new ActivationLayer(new SigmoidActivation(), name: "Sigmoid"));
            
            // 构建网络 - 只需指定输入通道数和初始尺寸
            Console.WriteLine("构建网络并自动推断张量大小...");
            network.Build(inputChannels: 3, inputHeight: 32, inputWidth: 32); // 3通道32x32图像
            
            // 生成一些随机训练数据
            Console.WriteLine("生成训练数据...");
            int sampleCount = 100;
            var inputs = new List<ITensor>();
            var targets = new List<ITensor>();
            
            var random = new Random();
            for (int i = 0; i < sampleCount; i++)
            {
                // 创建随机输入（3x32x32）
                var input = new Tensor(3, 32, 32);
                input.Randomize(-1, 1, random);
                inputs.Add(input);
                
                // 创建对应的目标（10个类别，使用one-hot编码）
                var target = new Tensor(10);
                target.Fill(0f);
                int classLabel = random.Next(0, 10); // 随机类别
                target[classLabel] = 1f;
                targets.Add(target);
            }
            
            // 训练网络
            Console.WriteLine($"{DateTime.Now} 开始训练...");
            network.Train(inputs, targets, epochs: 10, batchSize: 16);
            
            Console.WriteLine($"{DateTime.Now} 训练完成!");
            
            // 测试网络推理
            Console.WriteLine("测试网络推理...");
            var testInput = inputs[0];
            var prediction = network.Predict(testInput);
            
            Console.WriteLine("预测结果:");
            for (int i = 0; i < prediction.Size; i++)
            {
                Console.WriteLine($"类别 {i}: {prediction[i]:F4}");
            }
            
            int predictedClass = 0;
            float maxProbability = 0;
            for (int i = 0; i < prediction.Size; i++)
            {
                if (prediction[i] > maxProbability)
                {
                    maxProbability = prediction[i];
                    predictedClass = i;
                }
            }
            
            int actualClass = 0;
            for (int i = 0; i < targets[0].Size; i++)
            {
                if (targets[0][i] == 1f)
                {
                    actualClass = i;
                    break;
                }
            }
            
            Console.WriteLine($"预测类别: {predictedClass}, 实际类别: {actualClass}");
        }
    }
}
