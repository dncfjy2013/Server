namespace NeuralNetworkLibrary.Activations
{
    /// <summary>
    /// Leaky ReLU激活函数实现
    /// </summary>
    public class LeakyReLUActivation : IActivation
    {
        private float _alpha;

        public LeakyReLUActivation(float alpha = 0.01f)
        {
            _alpha = alpha;
        }

        public float Activate(float x)
        {
            return x > 0 ? x : _alpha * x;
        }

        public float Derivative(float x)
        {
            return x > 0 ? 1 : _alpha;
        }

        public float DerivativeFromOutput(float output)
        {
            return output > 0 ? 1 : _alpha;
        }
    }
}
