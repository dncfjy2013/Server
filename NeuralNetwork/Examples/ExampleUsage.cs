using System;
using System.Collections.Generic;
using NeuralNetwork.Layers;

namespace NeuralNetwork.Examples
{
    /// <summary>
    /// 神经网络使用示例
    /// </summary>
    public class ExampleUsage
    {
        public static void Run()
        {
            Console.WriteLine("创建一个简单的卷积神经网络...");
            
            // 创建一个用于分类任务的神经网络
            var network = new Network(LossType.CrossEntropy, 0.00001f);
            
            // 添加层 - 假设输入是 32x32 的 RGB 图像 (3, 32, 32)
            network.AddLayer(new ConvolutionalLayer(inputDepth: 3, outputDepth: 16, kernelSize: 3, stride: 1, PaddingType.Same));
            network.AddLayer(new ActivationLayer(ActivationType.ReLU));
            network.AddLayer(new PoolingLayer(poolSize: 2, stride: 2));
            
            network.AddLayer(new ConvolutionalLayer(inputDepth: 16, outputDepth: 32, kernelSize: 3, stride: 1, PaddingType.Same));
            network.AddLayer(new ActivationLayer(ActivationType.ReLU));
            network.AddLayer(new PoolingLayer(poolSize: 2, stride: 2));
            
            network.AddLayer(new FlattenLayer());
            network.AddLayer(new DenseLayer(inputSize: 32 * 8 * 8, outputSize: 128, ActivationType.ReLU));
            network.AddLayer(new DenseLayer(inputSize: 128, outputSize: 10, ActivationType.Softmax));
            
            Console.WriteLine($"神经网络创建完成，总参数数量: {network.TotalParameters:N0}");
            
            // 生成示例训练数据 (100个样本)
            var inputs = new List<float[,,]>();
            var targets = new List<float[,,]>();
            
            var random = new Random();
            
            for (int i = 0; i < 100; i++)
            {
                // 创建随机的32x32 RGB图像
                float[,,] input = new float[3, 32, 32];
                for (int d = 0; d < 3; d++)
                {
                    for (int h = 0; h < 32; h++)
                    {
                        for (int w = 0; w < 32; w++)
                        {
                            input[d, h, w] = (float)random.NextDouble();
                        }
                    }
                }
                
                // 创建随机的目标标签 (10类分类)
                float[,,] target = new float[10, 1, 1];
                int classLabel = random.Next(10);
                target[classLabel, 0, 0] = 1.0f;  // 独热编码
                
                inputs.Add(input);
                targets.Add(target);
            }
            
            Console.WriteLine("开始训练网络...");
            
            // 训练网络
            network.Train(inputs, targets, epochs: 100, batchSize: 16);
            
            Console.WriteLine("训练完成!");
            
            // 使用训练好的网络进行预测
            float[,,] testInput = inputs[0];
            float[,,] prediction = network.Predict(testInput);
            
            Console.WriteLine("\n测试预测结果:");
            for (int i = 0; i < prediction.GetLength(0); i++)
            {
                Console.WriteLine($"类别 {i}: {prediction[i, 0, 0]:F4}");
            }
        }
    }
}
