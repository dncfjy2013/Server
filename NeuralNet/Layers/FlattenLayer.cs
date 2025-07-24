using NeuralNetworkLibrary.Core;
using NeuralNetworkLibrary.Optimizers;
using System;

namespace NeuralNetworkLibrary.Layers
{
    /// <summary>
    /// 展平层，用于将多维张量转换为一维向量，通常用于卷积层和全连接层之间
    /// </summary>
    public class FlattenLayer : BaseLayer
    {
        public FlattenLayer(string name = "Flatten") : base(name) { }

        public override void SetInputShape(TensorShape inputShape)
        {
            InputShape = inputShape;
            
            // 计算展平后的大小
            int flattenedSize = 1;
            foreach (int dim in inputShape.Dimensions)
            {
                flattenedSize *= dim;
            }
            
            OutputShape = new TensorShape(flattenedSize);
        }

        public override void Initialize(Random random)
        {
            // 展平层没有参数需要初始化
        }

        public override ITensor Forward(ITensor input, bool isTraining = true)
        {
            var output = new Tensor(OutputShape.Dimensions);
            
            // 简单地复制数据（因为Tensor内部已经是扁平化存储）
            Array.Copy(input.Data, output.Data, input.Data.Length);
            
            return output;
        }

        public override ITensor Backward(ITensor gradient, float learningRate)
        {
            var inputGradient = new Tensor(InputShape.Dimensions);
            
            // 反向传播时同样只是复制数据
            Array.Copy(gradient.Data, inputGradient.Data, gradient.Data.Length);
            
            return inputGradient;
        }

    }
}
