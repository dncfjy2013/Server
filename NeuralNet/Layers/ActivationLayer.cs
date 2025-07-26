using NeuralNetworkLibrary.Activations;
using NeuralNetworkLibrary.Core;
using NeuralNetworkLibrary.Optimizers;
using System;
using System.Text.Json.Nodes;

namespace NeuralNetworkLibrary.Layers
{
    /// <summary>
    /// 激活层层实现
    /// </summary>
    public class ActivationLayer : BaseLayer
    {
        private IActivation _activation;
        private ITensor _input; // 缓存输入用于反向传播
        private ITensor _output;
        public override string LayerType => "ActivationLayer";
        public ActivationLayer(IActivation activation, string name = "Activation") : base(name)
        {
            _activation = activation;
        }

        public override void SetInputShape(TensorShape inputShape)
        {
            InputShape = inputShape;
            OutputShape = inputShape.Clone(); // 激活函数不改变形状
        }

        public override ITensor Forward(ITensor input, bool isTraining = true)
        {
            _input = isTraining ? input.Clone() : null;

            ITensor output = _activation.Activate(input);
            if (output == null)
                throw new InvalidOperationException("Activation function returned null output.");

            _output = isTraining ? output.Clone() : null;
            return output;
        }

        public override ITensor Backward(ITensor gradient, float learningRate)
        {          
            if(_output == null)
                throw new InvalidOperationException("Output is null, cannot compute backward pass without forward pass first.");
            if(_input == null)
                throw new InvalidOperationException("Input is null, cannot compute backward pass without forward pass first.");
            return _activation.DerivativeFromOutput(_output);
        }

        public override void UpdateParameters(IOptimizer optimizer)
        {

        }

        public override bool LoadParameters(JsonArray param)
        {
            try
            {
                // 激活层没有参数需要加载，仅验证结构是否匹配
                if (param.Count != 3)
                    return false;

                // 验证层类型和名称是否匹配
                return param[0]?.ToString() == "ActivationLayer" &&
                       param[1]?.ToString() == Name;
            }
            catch { return false; }
        }

        public override JsonArray GetParameters()
        {
            JsonArray parameters = new JsonArray();
            // 第1项：层类别
            parameters.Add("ActivationLayer");
            // 第2项：层名称
            parameters.Add(Name);
            // 第3项：参数（激活层无参数，设为null）
            parameters.Add(null);

            return parameters;
        }
        public override void Initialize(Random random)
        {
            // 激活层没有参数需要初始化
        }
        public override void ResetParameters(Random random)
        {

        }
    }
}
