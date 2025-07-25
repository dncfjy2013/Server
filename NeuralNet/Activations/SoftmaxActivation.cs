using NeuralNetworkLibrary.Core;
using System;

namespace NeuralNetworkLibrary.Activations
{
    /// <summary>
    /// Softmax激活函数实现，适用于多分类问题的输出层
    /// 功能：将输入张量转换为概率分布（所有元素和为1）
    /// </summary>
    public class SoftmaxActivation : IActivation
    {
        /// <summary>
        /// 计算Softmax激活值：σ(x)_i = exp(x_i - max_x) / Σ(exp(x_j - max_x))
        /// 加入max_x偏移量解决数值溢出问题
        /// </summary>
        public ITensor Activate(ITensor x)
        {
            // 1. 计算输入张量的最大值（提升数值稳定性）
            float maxVal = x.Max();

            // 2. 所有元素减去最大值（避免指数爆炸）
            ITensor xShifted = x.Clone();
            xShifted.Subtract(maxVal);

            // 3. 计算指数
            ITensor expTensor = xShifted.Clone();
            expTensor.Exp();  // 调用ITensor的元素级指数计算

            // 4. 计算指数和（用于归一化）
            float expSum = expTensor.Sum();

            // 5. 归一化得到概率分布
            ITensor softmax = expTensor.Clone();
            softmax.Divide(expSum);  // 每个元素除以总和

            return softmax;
        }

        /// <summary>
        /// 计算Softmax对输入的导数（简化版，适用于反向传播）
        /// 完整导数为Jacobian矩阵：∂σ_i/∂x_j = σ_i(δ_ij - σ_j)
        /// 此处返回对角线元素σ_i(1 - σ_i)，配合链式法则使用
        /// </summary>
        public ITensor Derivative(ITensor x)
        {
            // 1. 先计算激活值
            ITensor softmax = Activate(x);

            // 2. 计算σ_i(1 - σ_i)
            ITensor oneMinusSigma = softmax.Clone();
            oneMinusSigma.Negate();    // 等价于 0 - σ_i
            oneMinusSigma.Add(1f);     // 等价于 1 - σ_i
            softmax.Multiply(oneMinusSigma);  // 等价于 σ_i * (1 - σ_i)

            return softmax;
        }

        /// <summary>
        /// 从激活输出反推导数（效率更高）
        /// </summary>
        public ITensor DerivativeFromOutput(ITensor output)
        {
            // 直接使用激活输出计算：σ_i(1 - σ_i)
            ITensor oneMinusOutput = output.Clone();
            oneMinusOutput.Negate();
            oneMinusOutput.Add(1f);
            output.Multiply(oneMinusOutput);

            return output;
        }
    }
}