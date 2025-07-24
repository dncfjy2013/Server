using NeuralNetworkLibrary.Core;

namespace NeuralNetworkLibrary.LossFunctions
{
    /// <summary>
    /// 均方误差损失函数实现
    /// </summary>
    public class MSELoss : ILossFunction
    {
        public float Calculate(ITensor predicted, ITensor target)
        {
            if (predicted.Size != target.Size)
                throw new System.ArgumentException("Predicted and target tensors must have the same size");
                
            float sum = 0;
            for (int i = 0; i < predicted.Size; i++)
            {
                float diff = predicted.Data[i] - target.Data[i];
                sum += diff * diff;
            }
            
            return sum / predicted.Size;
        }

        public ITensor CalculateGradient(ITensor predicted, ITensor target)
        {
            if (predicted.Size != target.Size)
                throw new System.ArgumentException("Predicted and target tensors must have the same size");
                
            ITensor gradient = predicted.CreateLike();
            
            for (int i = 0; i < predicted.Size; i++)
            {
                gradient.Data[i] = 2 * (predicted.Data[i] - target.Data[i]) / predicted.Size;
            }
            
            return gradient;
        }
    }
}
