namespace NeuralNetwork
{
    /// <summary>
    /// 激活函数类型
    /// </summary>
    public enum ActivationType
    {
        ReLU,
        Sigmoid,
        Tanh,
        Softmax,
        LeakyReLU,
        None
    }

    /// <summary>
    /// 池化类型
    /// </summary>
    public enum PoolingType
    {
        Max,
        Average,
        Sum
    }

    /// <summary>
    /// 填充类型
    /// </summary>
    public enum PaddingType
    {
        Valid,  // 不填充
        Same    // 保持输出尺寸与输入相同
    }

    /// <summary>
    /// 优化器类型
    /// </summary>
    public enum OptimizerType
    {
        SGD,
        Adam,
        RMSprop,
        Adagrad
    }

    /// <summary>
    /// 损失函数类型
    /// </summary>
    public enum LossType
    {
        MeanSquaredError,
        CrossEntropy
    }
}
