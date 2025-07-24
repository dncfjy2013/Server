using NeuralNetworkLibrary.Core;

namespace NeuralNetworkLibrary.Optimizers
{
    /// <summary>
    /// 优化器接口
    /// </summary>
    public interface IOptimizer
    {
        float LearningRate { get; set; }
        float GradientClipThreshold { get; set; } // 梯度裁剪阈值，用于防止梯度爆炸
        
        void UpdateParameter(ITensor parameter, ITensor gradient);
        ITensor ApplyGradientClipping(ITensor gradient);
    }
}
