using NeuralNetworkLibrary.Activations;
using NeuralNetworkLibrary.Layers;
using NeuralNetworkLibrary.Optimizers;
using NeuralNetworkLibrary;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NeuralNetworkLibrary.LossFunctions;

namespace NeuralNet
{
    /// <summary>
    /// 残差网络示例（类似ResNet-18的简化版）
    /// 适用于CIFAR-10等3通道图像分类任务（32x32输入，10类输出）
    /// </summary>
    public class ResNetExample
    {
        public static NeuralNetwork CreateResNet18()
        {
            // 配置损失函数和优化器
            var loss = new CrossEntropyLoss();
            var optimizer = new AdamOptimizer(learningRate: 0.001f);
            var network = new NeuralNetwork(loss, optimizer);

            // 输入：3x32x32（CIFAR-10格式）
            int inputChannels = 3;
            int inputSize = 32;

            // 初始卷积层
            network.AddLayer(new ConvolutionLayer(
                kernelSize: 7,
                filters: 64,
                stride: 2,
                padding: 3,
                name: "initial_conv"
            ));
            network.AddLayer(new BatchNormalizationLayer(name: "initial_bn"));
            network.AddLayer(new ActivationLayer(new ReLUActivation(), name: "initial_relu"));

            // 第一个残差块组（不改变尺寸）
            int filters = 64;
            int stride = 1;
            network.AddLayer(new ResidualBlock(filters, stride, inputChannels, name: "res1_1"));
            network.AddLayer(new ResidualBlock(filters, stride, filters, name: "res1_2"));

            // 第二个残差块组（下采样）
            filters = 128;
            stride = 2;
            network.AddLayer(new ResidualBlock(filters, stride, 64, name: "res2_1"));
            network.AddLayer(new ResidualBlock(filters, 1, filters, name: "res2_2"));

            // 第三个残差块组（下采样）
            filters = 256;
            stride = 2;
            network.AddLayer(new ResidualBlock(filters, stride, 128, name: "res3_1"));
            network.AddLayer(new ResidualBlock(filters, 1, filters, name: "res3_2"));

            // 第四个残差块组（下采样）
            filters = 512;
            stride = 2;
            network.AddLayer(new ResidualBlock(filters, stride, 256, name: "res4_1"));
            network.AddLayer(new ResidualBlock(filters, 1, filters, name: "res4_2"));

            // 全局平均池化（将512x4x4转换为512x1x1）
            network.AddLayer(new PoolingLayer(
                poolSize: 4,
                stride: 1,
                poolingType: PoolingType.Average,
                name: "global_pool"
            ));

            // 展平层（512x1x1 → 512）
            network.AddLayer(new FlattenLayer(name: "flatten"));

            // 输出层（10类分类）
            network.AddLayer(new DenseLayer(units: 10, name: "fc_output"));
            network.AddLayer(new ActivationLayer(new SoftmaxActivation(), name: "softmax"));

            // 构建网络
            network.Build(inputChannels, inputSize, inputSize);
            return network;
        }

        // 示例用法
        public static void Run()
        {
            // 创建残差网络
            var resnet = CreateResNet18();
            Console.WriteLine("残差网络构建完成！");

            // 此处可添加训练代码（示例）
            // 假设已准备好CIFAR-10数据集的inputs和targets
            // resnet.Train(inputs, targets, epochs: 50, batchSize: 64);
        }
    }
}
