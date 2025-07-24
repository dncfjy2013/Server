namespace NeuralNetworkLibrary.Activations
{
    /// <summary>
    /// 激活函数接口
    /// </summary>
    public interface IActivation
    {
        float Activate(float x);
        float Derivative(float x);
        float DerivativeFromOutput(float output);
    }
}
