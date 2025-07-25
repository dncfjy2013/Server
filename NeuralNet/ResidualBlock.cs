using NeuralNetworkLibrary.Activations;
using NeuralNetworkLibrary.Core;
using NeuralNetworkLibrary.Layers;
using NeuralNetworkLibrary.Optimizers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace NeuralNet
{
    /// <summary>
    /// 残差块（Residual Block），包含跳跃连接
    /// </summary>
    public class ResidualBlock : BaseLayer
    {
        private ILayer _conv1;
        private ILayer _bn1;
        private ILayer _act1;
        private ILayer _conv2;
        private ILayer _bn2;
        private ILayer _shortcut; // 用于维度调整的捷径连接（1x1卷积）
        private ILayer _act2;

        public override bool HasParameters => true;

        /// <summary>
        /// 初始化残差块
        /// </summary>
        /// <param name="filters">输出通道数</param>
        /// <param name="stride">步长（用于下采样）</param>
        /// <param name="inputChannels">输入通道数（用于判断是否需要维度调整）</param>
        /// <param name="name">块名称</param>
        public ResidualBlock(int filters, int stride, int inputChannels, string name = "ResBlock")
            : base(name)
        {
            // 第一个卷积层：3x3卷积
            _conv1 = new ConvolutionLayer(3, filters, stride, padding: 1, $"{name}_conv1");
            _bn1 = new BatchNormalizationLayer(name: $"{name}_bn1");
            _act1 = new ActivationLayer(new ReLUActivation(), $"{name}_relu1");

            // 第二个卷积层：3x3卷积（步长固定为1）
            _conv2 = new ConvolutionLayer(3, filters, stride: 1, padding: 1, $"{name}_conv2");
            _bn2 = new BatchNormalizationLayer(name: $"{name}_bn2");

            // 捷径连接：如果输入输出通道数不同或需要下采样，使用1x1卷积调整
            if (stride != 1 || inputChannels != filters)
            {
                _shortcut = new ConvolutionLayer(1, filters, stride, padding: 0, $"{name}_shortcut");
            }

            // 最终激活函数
            _act2 = new ActivationLayer(new ReLUActivation(), $"{name}_relu2");
        }

        public override void SetInputShape(TensorShape inputShape)
        {
            InputShape = inputShape;

            // 传播输入形状到内部层
            _conv1.SetInputShape(inputShape);
            _bn1.SetInputShape(_conv1.OutputShape);
            _act1.SetInputShape(_bn1.OutputShape);
            _conv2.SetInputShape(_act1.OutputShape);
            _bn2.SetInputShape(_conv2.OutputShape);

            // 如果有捷径连接，设置其输入形状
            if (_shortcut != null)
            {
                _shortcut.SetInputShape(inputShape);
            }

            // 输出形状与第二个BN层一致
            OutputShape = _bn2.OutputShape;
        }

        public override void Initialize(Random random)
        {
            _conv1.Initialize(random);
            _bn1.Initialize(random);
            _act1.Initialize(random);
            _conv2.Initialize(random);
            _bn2.Initialize(random);
            _shortcut?.Initialize(random);
            _act2.Initialize(random);
        }

        public override ITensor Forward(ITensor input, bool isTraining = true)
        {
            // 主路径
            ITensor mainPath = _conv1.Forward(input, isTraining);
            mainPath = _bn1.Forward(mainPath, isTraining);
            mainPath = _act1.Forward(mainPath, isTraining);

            mainPath = _conv2.Forward(mainPath, isTraining);
            mainPath = _bn2.Forward(mainPath, isTraining);

            // 捷径连接
            ITensor shortcut = _shortcut != null
                ? _shortcut.Forward(input, isTraining)
                : input; // 维度匹配时直接使用输入

            // 残差连接：主路径 + 捷径
            mainPath.Add(shortcut);

            // 最终激活
            return _act2.Forward(mainPath, isTraining);
        }

        public override ITensor Backward(ITensor gradient, float learningRate)
        {
            // 反向传播通过最终激活
            ITensor grad = _act2.Backward(gradient, learningRate);

            // 残差梯度需要拆分到主路径和捷径
            ITensor mainGrad = _bn2.Backward(grad, learningRate);
            mainGrad = _conv2.Backward(mainGrad, learningRate);
            mainGrad = _act1.Backward(mainGrad, learningRate);
            mainGrad = _bn1.Backward(mainGrad, learningRate);
            mainGrad = _conv1.Backward(mainGrad, learningRate);

            // 捷径路径的反向传播
            ITensor shortcutGrad = _shortcut != null
                ? _shortcut.Backward(grad, learningRate)
                : grad; // 无捷径卷积时直接传递梯度

            // 合并主路径和捷径的梯度
            mainGrad.Add(shortcutGrad);
            return mainGrad;
        }

        public override void UpdateParameters(IOptimizer optimizer)
        {
            _conv1.UpdateParameters(optimizer);
            _bn1.UpdateParameters(optimizer);
            _conv2.UpdateParameters(optimizer);
            _bn2.UpdateParameters(optimizer);
            _shortcut?.UpdateParameters(optimizer);
        }

        public override bool LoadParameters(JsonArray param)
        {
            if (param.Count != 6) return false; // 6个内部层参数
            return _conv1.LoadParameters(param[0] as JsonArray) &&
                    _bn1.LoadParameters(param[1] as JsonArray) &&
                    _act1.LoadParameters(param[2] as JsonArray) &&
                    _conv2.LoadParameters(param[3] as JsonArray) &&
                    _bn2.LoadParameters(param[4] as JsonArray) &&
                    (_shortcut == null || _shortcut.LoadParameters(param[5] as JsonArray));
        }

        public override JsonArray GetParameters()
        {
            return new JsonArray
        {
            _conv1.GetParameters(),
            _bn1.GetParameters(),
            _act1.GetParameters(),
            _conv2.GetParameters(),
            _bn2.GetParameters(),
            _shortcut?.GetParameters()
        };
        }
    }
}
