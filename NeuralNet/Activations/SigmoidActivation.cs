using NeuralNetworkLibrary.Core;
using System;
using System.Numerics;

namespace NeuralNetworkLibrary.Activations
{
    /// <summary>
    /// Sigmoid激活函数实现（优化版）
    /// 公式：σ(x) = 1 / (1 + exp(-x))
    /// 导数：σ'(x) = σ(x) * (1 - σ(x))
    /// </summary>
    public class SigmoidActivation : IActivation
    {
        /// <summary>
        /// 前向计算Sigmoid激活值（利用ITensor内置方法和SIMD加速）
        /// </summary>
        public ITensor Activate(ITensor input)
        {
            // 1. 克隆输入张量（避免修改原始数据）
            ITensor output = input.Clone();

            // 2. 计算 -x（利用张量的Negate方法）
            output.Negate();

            // 3. 计算 exp(-x)（利用ITensor的Exp方法，底层可能已实现SIMD加速）
            output.Exp();

            // 4. 计算 1 + exp(-x)
            output.Add(1f);

            // 5. 计算 1 / (1 + exp(-x))
            output.Divide(1f);  // 等价于输出 = 1 / 输出

            return output;
        }

        /// <summary>
        /// 计算Sigmoid对输入x的导数
        /// </summary>
        public ITensor Derivative(ITensor x)
        {
            // 1. 先计算Sigmoid激活值
            ITensor sigmoid = Activate(x);

            // 2. 计算 1 - σ(x)
            ITensor oneMinusSigmoid = sigmoid.Clone();
            oneMinusSigmoid.Negate();  // -σ(x)
            oneMinusSigmoid.Add(1f);   // 1 - σ(x)

            // 3. 计算 σ(x) * (1 - σ(x))
            sigmoid.Multiply(oneMinusSigmoid);

            return sigmoid;
        }

        /// <summary>
        /// 从激活输出直接计算导数（更高效，无需重新计算Sigmoid）
        /// </summary>
        public ITensor DerivativeFromOutput(ITensor output)
        {
            // 1. 克隆输出张量（避免修改原始激活值）
            ITensor derivative = output.Clone();

            // 2. 计算 1 - output（复用输出张量）
            ITensor oneMinusOutput = output.Clone();
            oneMinusOutput.Negate();
            oneMinusOutput.Add(1f);

            // 3. 计算 output * (1 - output)
            derivative.Multiply(oneMinusOutput);

            return derivative;
        }
    }
}