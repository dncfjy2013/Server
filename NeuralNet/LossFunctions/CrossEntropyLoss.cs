using NeuralNetworkLibrary.Core;
using System;

namespace NeuralNetworkLibrary.LossFunctions
{
    /// <summary>
    /// 交叉熵损失函数实现，通常用于分类问题
    /// </summary>
    public class CrossEntropyLoss : ILossFunction
    {
        public float Calculate(ITensor predicted, ITensor target)
        {
            if (predicted.Size != target.Size)
                throw new ArgumentException("Predicted and target tensors must have the same size");
                
            float loss = 0;
            float epsilon = 1e-10f; // 防止log(0)
            
            for (int i = 0; i < predicted.Size; i++)
            {
                // 确保预测值在有效范围内
                float p = Math.Max(epsilon, Math.Min(1 - epsilon, predicted.Data[i]));
                loss -= target.Data[i] * (float)Math.Log(p) + (1 - target.Data[i]) * (float)Math.Log(1 - p);
            }
            
            return loss / predicted.Size;
        }

        public ITensor CalculateGradient(ITensor predicted, ITensor target)
        {
            if (predicted.Size != target.Size)
                throw new ArgumentException("Predicted and target tensors must have the same size");
                
            ITensor gradient = predicted.CreateLike();
            float epsilon = 1e-10f;
            
            for (int i = 0; i < predicted.Size; i++)
            {
                // 确保预测值在有效范围内
                float p = Math.Max(epsilon, Math.Min(1 - epsilon, predicted.Data[i]));
                gradient.Data[i] = (p - target.Data[i]) / (p * (1 - p) * predicted.Size);
            }
            
            return gradient;
        }
    }
}
