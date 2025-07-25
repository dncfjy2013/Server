using NeuralNetworkLibrary.Core;

namespace NeuralNetworkLibrary.Activations
{
    /// <summary>
    /// ReLU激活函数（基于扩展ITensor的高效实现）
    /// </summary>
    public class ReLUActivation : IActivation
    {
        /// <summary>
        /// 前向计算：f(x) = max(0, x)
        /// </summary>
        public ITensor Activate(ITensor input)
        {
            // 利用ITensor新增的ApplyAndClone方法，直接返回激活结果
            return input.ApplyAndClone(x => Math.Max(x, 0f));
        }

        /// <summary>
        /// 导数计算：f'(x) = 1 if x > 0 else 0
        /// </summary>
        public ITensor Derivative(ITensor input)
        {
            // 对输入应用ReLU导数规则
            return input.ApplyAndClone(x => x > 0f ? 1f : 0f);
        }

        /// <summary>
        /// 从输出反推导数（ReLU输出与输入同号，直接用输出计算）
        /// </summary>
        public ITensor DerivativeFromOutput(ITensor output)
        {
            // 复用Derivative的逻辑，直接基于输出计算
            return output.ApplyAndClone(x => x > 0f ? 1f : 0f);
        }
    }
}