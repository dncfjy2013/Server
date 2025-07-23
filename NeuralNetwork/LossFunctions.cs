using System;

namespace NeuralNetwork
{
    /// <summary>
    /// 均方误差损失函数
    /// </summary>
    public class MeanSquaredErrorLoss : ILossFunction
    {
        /// <summary>
        /// 计算均方误差损失
        /// </summary>
        public float CalculateLoss(float[,,] predictions, float[,,] targets)
        {
            ValidateShapes(predictions, targets);
            
            int depth = predictions.GetLength(0);
            int height = predictions.GetLength(1);
            int width = predictions.GetLength(2);
            
            float sum = 0;
            int count = 0;
            
            for (int d = 0; d < depth; d++)
            {
                for (int h = 0; h < height; h++)
                {
                    for (int w = 0; w < width; w++)
                    {
                        float diff = predictions[d, h, w] - targets[d, h, w];
                        sum += diff * diff;
                        count++;
                    }
                }
            }
            
            return sum / count;
        }
        
        /// <summary>
        /// 计算均方误差损失的梯度
        /// </summary>
        public float[,,] CalculateGradient(float[,,] predictions, float[,,] targets)
        {
            ValidateShapes(predictions, targets);
            
            int depth = predictions.GetLength(0);
            int height = predictions.GetLength(1);
            int width = predictions.GetLength(2);
            
            float[,,] gradient = new float[depth, height, width];
            int count = depth * height * width;
            
            for (int d = 0; d < depth; d++)
            {
                for (int h = 0; h < height; h++)
                {
                    for (int w = 0; w < width; w++)
                    {
                        gradient[d, h, w] = 2 * (predictions[d, h, w] - targets[d, h, w]) / count;
                    }
                }
            }
            
            return gradient;
        }
        
        /// <summary>
        /// 验证预测和目标的形状是否一致
        /// </summary>
        private void ValidateShapes(float[,,] predictions, float[,,] targets)
        {
            if (predictions.GetLength(0) != targets.GetLength(0) ||
                predictions.GetLength(1) != targets.GetLength(1) ||
                predictions.GetLength(2) != targets.GetLength(2))
            {
                throw new ArgumentException("Predictions and targets must have the same shape");
            }
        }
    }
    
    /// <summary>
    /// 交叉熵损失函数
    /// </summary>
    public class CrossEntropyLoss : ILossFunction
    {
        private const float Epsilon = 1e-10f;  // 防止log(0)
        
        /// <summary>
        /// 计算交叉熵损失
        /// </summary>
        public float CalculateLoss(float[,,] predictions, float[,,] targets)
        {
            ValidateShapes(predictions, targets);
            
            int depth = predictions.GetLength(0);
            int height = predictions.GetLength(1);
            int width = predictions.GetLength(2);
            
            // 交叉熵通常用于分类问题，期望输入是 (classes, 1, 1) 形状
            if (height != 1 || width != 1)
            {
                throw new ArgumentException("Cross entropy loss expects 1D feature vectors (depth, 1, 1)");
            }
            
            float loss = 0;
            
            for (int d = 0; d < depth; d++)
            {
                // 添加小值防止log(0)
                float p = Math.Max(predictions[d, 0, 0], Epsilon);
                loss -= targets[d, 0, 0] * (float)Math.Log(p);
            }
            
            return loss;
        }
        
        /// <summary>
        /// 计算交叉熵损失的梯度
        /// </summary>
        public float[,,] CalculateGradient(float[,,] predictions, float[,,] targets)
        {
            ValidateShapes(predictions, targets);
            
            int depth = predictions.GetLength(0);
            int height = predictions.GetLength(1);
            int width = predictions.GetLength(2);
            
            float[,,] gradient = new float[depth, height, width];
            
            for (int d = 0; d < depth; d++)
            {
                for (int h = 0; h < height; h++)
                {
                    for (int w = 0; w < width; w++)
                    {
                        // 添加小值防止除以0
                        float p = Math.Max(predictions[d, h, w], Epsilon);
                        gradient[d, h, w] = -targets[d, h, w] / p;
                    }
                }
            }
            
            return gradient;
        }
        
        /// <summary>
        /// 验证预测和目标的形状是否一致
        /// </summary>
        private void ValidateShapes(float[,,] predictions, float[,,] targets)
        {
            if (predictions.GetLength(0) != targets.GetLength(0) ||
                predictions.GetLength(1) != targets.GetLength(1) ||
                predictions.GetLength(2) != targets.GetLength(2))
            {
                throw new ArgumentException("Predictions and targets must have the same shape");
            }
        }
    }
}
