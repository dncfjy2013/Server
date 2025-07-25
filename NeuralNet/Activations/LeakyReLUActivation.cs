using NeuralNetworkLibrary.Core;

namespace NeuralNetworkLibrary.Activations
{
    /// <summary>
    /// Leaky ReLU激活函数（优化版）
    /// 公式：f(x) = x if x > 0 else α·x（α为小常数，默认0.01）
    /// 导数：f'(x) = 1 if x > 0 else α
    /// </summary>
    public class LeakyReLUActivation : IActivation
    {
        private readonly float _alpha; // 负区间斜率（默认0.01）

        public LeakyReLUActivation(float alpha = 0.01f)
        {
            _alpha = alpha;
        }

        /// <summary>
        /// 前向计算Leaky ReLU激活值（利用ITensor的ApplyAndClone方法）
        /// </summary>
        public ITensor Activate(ITensor input)
        {
            // 对每个元素应用Leaky ReLU规则：x > 0 → x，否则 → α·x
            return input.ApplyAndClone(x => x > 0f ? x : _alpha * x);
        }

        /// <summary>
        /// 计算Leaky ReLU对输入x的导数
        /// </summary>
        public ITensor Derivative(ITensor input)
        {
            // 导数规则：x > 0 → 1，否则 → α
            return input.ApplyAndClone(x => x > 0f ? 1f : _alpha);
        }

        /// <summary>
        /// 从激活输出反推导数（利用输出值的符号判断）
        /// </summary>
        public ITensor DerivativeFromOutput(ITensor output)
        {
            // Leaky ReLU输出特性：output > 0 等价于 input > 0，因此可直接用输出计算
            return output.ApplyAndClone(x => x > 0f ? 1f : _alpha);
        }
    }
}