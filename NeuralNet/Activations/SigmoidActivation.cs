namespace NeuralNetworkLibrary.Activations
{
    /// <summary>
    /// Sigmoid激活函数实现
    /// </summary>
    public class SigmoidActivation : IActivation
    {
        public float Activate(float x)
        {
            return 1.0f / (1.0f + (float)System.Math.Exp(-x));
        }

        public float Derivative(float x)
        {
            float sigmoid = Activate(x);
            return sigmoid * (1 - sigmoid);
        }

        public float DerivativeFromOutput(float output)
        {
            return output * (1 - output);
        }
    }
}
