using System;

namespace NeuralNetwork
{
    /// <summary>
    /// 神经网络层接口
    /// </summary>
    public interface ILayer
    {
        /// <summary>
        /// 层名称
        /// </summary>
        string Name { get; set; }

        /// <summary>
        /// 前向传播
        /// </summary>
        /// <param name="input">输入数据</param>
        /// <returns>输出数据</returns>
        float[,,] Forward(float[,,] input);

        /// <summary>
        /// 反向传播
        /// </summary>
        /// <param name="outputGradient">输出梯度</param>
        /// <param name="learningRate">学习率</param>
        /// <returns>输入梯度</returns>
        float[,,] Backward(float[,,] outputGradient, float learningRate);

        /// <summary>
        /// 获取层的参数数量
        /// </summary>
        int ParameterCount { get; }
    }

    /// <summary>
    /// 优化器接口
    /// </summary>
    public interface IOptimizer
    {
        /// <summary>
        /// 更新参数
        /// </summary>
        /// <param name="parameter">参数</param>
        /// <param name="gradient">梯度</param>
        /// <param name="parameterId">参数唯一标识</param>
        void Update(ref float parameter, float gradient, string parameterId);
    }

    /// <summary>
    /// 损失函数接口
    /// </summary>
    public interface ILossFunction
    {
        /// <summary>
        /// 计算损失
        /// </summary>
        /// <param name="predictions">预测值</param>
        /// <param name="targets">目标值</param>
        /// <returns>损失值</returns>
        float CalculateLoss(float[,,] predictions, float[,,] targets);

        /// <summary>
        /// 计算损失梯度
        /// </summary>
        /// <param name="predictions">预测值</param>
        /// <param name="targets">目标值</param>
        /// <returns>梯度</returns>
        float[,,] CalculateGradient(float[,,] predictions, float[,,] targets);
    }
}
