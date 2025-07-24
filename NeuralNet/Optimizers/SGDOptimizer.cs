using NeuralNetworkLibrary.Core;
using System;

namespace NeuralNetworkLibrary.Optimizers
{
    /// <summary>
    /// 随机梯度下降优化器实现
    /// </summary>
    public class SGDOptimizer : IOptimizer
    {
        public float LearningRate { get; set; }
        public float Momentum { get; set; }
        public float GradientClipThreshold { get; set; }
        
        private Dictionary<string, ITensor> _momentum;

        public SGDOptimizer(float learningRate = 0.01f, float momentum = 0.9f, float gradientClipThreshold = 1.0f)
        {
            LearningRate = learningRate;
            Momentum = momentum;
            GradientClipThreshold = gradientClipThreshold;
            _momentum = new Dictionary<string, ITensor>();
        }

        public void UpdateParameter(ITensor parameter, ITensor gradient)
        {
            string paramId = parameter.GetHashCode().ToString();
            
            // 应用梯度裁剪
            ITensor clippedGradient = ApplyGradientClipping(gradient);
            
            // 初始化动量（如果不存在）
            if (!_momentum.ContainsKey(paramId))
            {
                _momentum[paramId] = parameter.CreateLike();
                _momentum[paramId].Fill(0f);
            }
            
            // 更新动量
            for (int i = 0; i < parameter.Size; i++)
            {
                _momentum[paramId].Data[i] = Momentum * _momentum[paramId].Data[i] - LearningRate * clippedGradient.Data[i];
                parameter.Data[i] += _momentum[paramId].Data[i];
            }
        }
        
        public ITensor ApplyGradientClipping(ITensor gradient)
        {
            if (GradientClipThreshold <= 0)
                return gradient;
                
            // 计算梯度的L2范数
            float norm = 0;
            for (int i = 0; i < gradient.Size; i++)
            {
                norm += gradient.Data[i] * gradient.Data[i];
            }
            norm = (float)Math.Sqrt(norm);
            
            // 如果范数超过阈值，则裁剪
            ITensor clippedGradient = gradient.Clone();
            if (norm > GradientClipThreshold)
            {
                float scale = GradientClipThreshold / norm;
                for (int i = 0; i < gradient.Size; i++)
                {
                    clippedGradient.Data[i] *= scale;
                }
            }
            
            return clippedGradient;
        }
    }
}
