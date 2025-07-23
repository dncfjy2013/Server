namespace NeuralNetwork.Layers
{
    /// <summary>
    /// 激活层，专门用于应用激活函数
    /// </summary>
    public class ActivationLayer : ILayer
    {
        private readonly ActivationType _activationType;
        private float[,,] _inputCache;  // 缓存输入用于反向传播
        
        public string Name { get; set; }
        public int ParameterCount => 0;  // 激活层没有可训练参数

        /// <summary>
        /// 初始化激活层
        /// </summary>
        /// <param name="activationType">激活函数类型</param>
        public ActivationLayer(ActivationType activationType)
        {
            _activationType = activationType;
            Name = $"Activation_{activationType}";
        }
        
        /// <summary>
        /// 前向传播
        /// </summary>
        public float[,,] Forward(float[,,] input)
        {
            _inputCache = (float[,,])input.Clone();
            
            if (_activationType == ActivationType.Softmax)
            {
                return ActivationFunctions.Softmax(input);
            }
            
            return ActivationFunctions.ApplyActivation(input, _activationType);
        }
        
        /// <summary>
        /// 反向传播
        /// </summary>
        public float[,,] Backward(float[,,] outputGradient, float learningRate)
        {
            int depth = _inputCache.GetLength(0);
            int height = _inputCache.GetLength(1);
            int width = _inputCache.GetLength(2);
            
            float[,,] inputGradient = new float[depth, height, width];
            float[,,] derivatives = ActivationFunctions.ApplyActivationDerivative(_inputCache, _activationType);
            
            // 计算输入梯度 = 输出梯度 * 激活函数导数
            for (int d = 0; d < depth; d++)
            {
                for (int h = 0; h < height; h++)
                {
                    for (int w = 0; w < width; w++)
                    {
                        inputGradient[d, h, w] = outputGradient[d, h, w] * derivatives[d, h, w];
                    }
                }
            }
            
            return inputGradient;
        }
    }
}
