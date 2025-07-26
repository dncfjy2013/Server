using NeuralNetworkLibrary.Core;
using NeuralNetworkLibrary.Optimizers;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace NeuralNetworkLibrary.Layers
{
    /// <summary>
    /// 所有网络层的基类
    /// </summary>
    public abstract class BaseLayer : ILayer
    {
        public string Name { get; }
        public TensorShape InputShape { get; protected set; }
        public TensorShape OutputShape { get; protected set; }
        public double ForwardTimeMs { get; protected set; }
        public double BackwardTimeMs { get; protected set; }
        public virtual bool HasParameters => false;
        public virtual string LayerType => null;

        public float Version => 1.0f;

        public IDictionary<string, string> Metadata => new Dictionary<string, string>() 
        {
            { "Author", "NeuralNetworkLibrary" },
            { "Description", "Base layer for neural networks" },
            { "Version", Version.ToString() }
        };

        public string Device { get; protected set; } = "cpu";

        public PrecisionMode Precision { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public QuantizationMode Quantization { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        protected BaseLayer(string name)
        {
            Name = name;
        }

        public abstract void Initialize(Random random);
        public abstract void SetInputShape(TensorShape inputShape);
        public abstract ITensor Forward(ITensor input, bool isTraining = true);
        public abstract ITensor Backward(ITensor gradient, float learningRate);
        public abstract void UpdateParameters(IOptimizer optimizer);
        public abstract bool LoadParameters(JsonArray param);
        public abstract JsonArray GetParameters();
        public abstract void ResetParameters(Random random);

        public void MoveToDevice(string device)
        {
            if (string.IsNullOrEmpty(device))
            {
                Device = "cpu";
                return;
            }

            // 1. 去除所有空格（包括空格、制表符等空白字符）
            string trimmed = Regex.Replace(device, @"\s+", "");

            // 2. 转换为小写
            string normalizedDevice = trimmed.ToLowerInvariant();

            // 3. 赋值给设备属性
            Device = normalizedDevice;
        }

        public void SetPrecision(PrecisionMode mode)
        {
            throw new NotImplementedException();
        }

        public void ApplyConstraints()
        {
            throw new NotImplementedException();
        }
    }
}
