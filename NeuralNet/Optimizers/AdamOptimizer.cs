using NeuralNetworkLibrary.Core;
using System;
using System.Collections.Generic;

namespace NeuralNetworkLibrary.Optimizers
{
    /// <summary>
    /// Adam优化器实现，带有梯度裁剪功能防止梯度爆炸
    /// </summary>
    public class AdamOptimizer : IOptimizer
    {
        public float LearningRate { get; set; }
        public float Beta1 { get; set; }
        public float Beta2 { get; set; }
        public float Epsilon { get; set; }
        public float GradientClipThreshold { get; set; }
        
        private int _step;
        private Dictionary<string, ITensor> _m; // 一阶矩估计
        private Dictionary<string, ITensor> _v; // 二阶矩估计

        public AdamOptimizer(float learningRate = 0.001f, float beta1 = 0.9f, float beta2 = 0.999f, 
                            float epsilon = 1e-8f, float gradientClipThreshold = 1.0f)
        {
            LearningRate = learningRate;
            Beta1 = beta1;
            Beta2 = beta2;
            Epsilon = epsilon;
            GradientClipThreshold = gradientClipThreshold;
            _step = 0;
            _m = new Dictionary<string, ITensor>();
            _v = new Dictionary<string, ITensor>();
        }

        public void UpdateParameter(ITensor parameter, ITensor gradient)
        {
            _step++;
            string paramId = parameter.GetHashCode().ToString();
            
            // 应用梯度裁剪
            ITensor clippedGradient = ApplyGradientClipping(gradient);
            
            // 初始化一阶矩和二阶矩（如果不存在）
            if (!_m.ContainsKey(paramId))
            {
                _m[paramId] = parameter.CreateLike();
                _m[paramId].Fill(0f);
                
                _v[paramId] = parameter.CreateLike();
                _v[paramId].Fill(0f);
            }
            
            // 更新一阶矩和二阶矩
            for (int i = 0; i < parameter.Size; i++)
            {
                _m[paramId].Data[i] = Beta1 * _m[paramId].Data[i] + (1 - Beta1) * clippedGradient.Data[i];
                _v[paramId].Data[i] = Beta2 * _v[paramId].Data[i] + (1 - Beta2) * clippedGradient.Data[i] * clippedGradient.Data[i];
            }
            
            // 计算偏差修正的一阶矩和二阶矩
            float correctedBeta1 = 1 - (float)Math.Pow(Beta1, _step);
            float correctedBeta2 = 1 - (float)Math.Pow(Beta2, _step);
            float learningRateCorrected = LearningRate * (float)Math.Sqrt(correctedBeta2) / correctedBeta1;
            
            // 更新参数
            for (int i = 0; i < parameter.Size; i++)
            {
                float mHat = _m[paramId].Data[i] / correctedBeta1;
                float vHat = _v[paramId].Data[i] / correctedBeta2;
                parameter.Data[i] -= learningRateCorrected * mHat / ((float)Math.Sqrt(vHat) + Epsilon);
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
