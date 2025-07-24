using NeuralNetworkLibrary.Core;
using NeuralNetworkLibrary.Optimizers;
using System.Text.Json.Nodes;

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
        public virtual bool HasParameters => false;

        public string LayerType => throw new NotImplementedException();

        public int Version => throw new NotImplementedException();

        public IDictionary<string, string> Metadata => throw new NotImplementedException();

        public string Device => throw new NotImplementedException();

        public bool IsTraining => throw new NotImplementedException();

        public PrecisionMode Precision { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public QuantizationMode Quantization { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public IReadOnlyList<ILayer> NextLayers => throw new NotImplementedException();

        public IReadOnlyList<ILayer> PreviousLayers => throw new NotImplementedException();

        public double ForwardTimeMs => throw new NotImplementedException();

        public double BackwardTimeMs => throw new NotImplementedException();

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

        public void SetTraining(bool isTraining)
        {
            throw new NotImplementedException();
        }

        public void MoveToDevice(string device)
        {
            throw new NotImplementedException();
        }

        public void SetPrecision(PrecisionMode mode)
        {
            throw new NotImplementedException();
        }

        public IReadOnlyList<string> GetParameterNames()
        {
            throw new NotImplementedException();
        }

        public ITensor GetParameter(string name)
        {
            throw new NotImplementedException();
        }

        public void ApplyConstraints()
        {
            throw new NotImplementedException();
        }

        public void ResetParameters(Random random)
        {
            throw new NotImplementedException();
        }

        public IReadOnlyDictionary<string, ITensor> GetStates()
        {
            throw new NotImplementedException();
        }

        public void SetStates(IDictionary<string, ITensor> states)
        {
            throw new NotImplementedException();
        }

        public void ResetStates()
        {
            throw new NotImplementedException();
        }

        public JsonNode SaveConfiguration()
        {
            throw new NotImplementedException();
        }

        public bool LoadConfiguration(JsonNode config)
        {
            throw new NotImplementedException();
        }

        public byte[] ExportParameters(SerializationFormat format = SerializationFormat.Binary)
        {
            throw new NotImplementedException();
        }

        public bool ImportParameters(byte[] data, SerializationFormat format = SerializationFormat.Binary)
        {
            throw new NotImplementedException();
        }

        public void OptimizeForInference()
        {
            throw new NotImplementedException();
        }

        public GraphNode GetGraphNode()
        {
            throw new NotImplementedException();
        }

        public IDictionary<string, ITensor> GetDebugIntermediates()
        {
            throw new NotImplementedException();
        }

        public ITensor ComputeInputGradient(ITensor input, int outputIndex = -1)
        {
            throw new NotImplementedException();
        }

        public void AddNextLayer(ILayer layer)
        {
            throw new NotImplementedException();
        }

        public void RemoveNextLayer(ILayer layer)
        {
            throw new NotImplementedException();
        }

        public void ResetPerformanceStats()
        {
            throw new NotImplementedException();
        }
    }
}
