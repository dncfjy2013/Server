namespace NeuralNetworkLibrary.Activations
{
    /// <summary>
    /// ReLU激活函数实现
    /// </summary>
    public class ReLUActivation : IActivation
    {
        public float Activate(float x)
        {
            return x > 0 ? x : 0;
        }

        public float Derivative(float x)
        {
            return x > 0 ? 1 : 0;
        }

        public float DerivativeFromOutput(float output)
        {
            // 对于ReLU，输出与输入的导数关系相同
            return output > 0 ? 1 : 0;
        }
    }
}
