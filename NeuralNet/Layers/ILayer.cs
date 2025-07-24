using NeuralNetworkLibrary.Core;
using NeuralNetworkLibrary.Optimizers;
using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace NeuralNetworkLibrary.Layers
{
    /// <summary>
    /// 增强版神经网络层接口，支持复杂训练流程、模型优化和部署
    /// </summary>
    public interface ILayer
    {
        #region 基础元信息
        string Name { get; }
        TensorShape InputShape { get; }
        TensorShape OutputShape { get; }
        bool HasParameters { get; }
        string LayerType { get; }  // 层类型标识（如"Conv2D"、"LSTM"）
        int Version { get; }       // 版本号（用于兼容性处理）
        IDictionary<string, string> Metadata { get; }  // 附加元数据（如作者、描述）
        #endregion

        #region 计算模式与设备
        string Device { get; }  // 运行设备（"cpu"、"gpu:0"等）
        bool IsTraining { get; }
        PrecisionMode Precision { get; set; }  // 计算精度（FP32/FP16/INT8）
        QuantizationMode Quantization { get; set; }  // 量化模式（仅推理）

        void SetTraining(bool isTraining);
        void MoveToDevice(string device);
        void SetPrecision(PrecisionMode mode);
        #endregion

        #region 核心计算流程
        void Initialize(Random random);
        void SetInputShape(TensorShape inputShape);
        ITensor Forward(ITensor input, bool isTraining = true);
        ITensor Backward(ITensor gradient, float learningRate);
        void UpdateParameters(IOptimizer optimizer);
        #endregion

        #region 参数管理与约束
        JsonArray GetParameters();
        bool LoadParameters(JsonArray param);
        /// <summary>
        /// 获取所有可训练参数的名称列表
        /// </summary>
        IReadOnlyList<string> GetParameterNames();

        /// <summary>
        /// 通过名称获取参数张量（用于自定义优化或检查）
        /// </summary>
        ITensor GetParameter(string name);

        /// <summary>
        /// 对参数施加约束（如L2正则化、权重裁剪）
        /// </summary>
        void ApplyConstraints();

        /// <summary>
        /// 重置参数为初始状态
        /// </summary>
        void ResetParameters(Random random);
        #endregion

        #region 状态管理（针对循环层等有状态层）
        /// <summary>
        /// 获取当前层的内部状态（如LSTM的隐藏状态）
        /// </summary>
        IReadOnlyDictionary<string, ITensor> GetStates();

        /// <summary>
        /// 设置层的内部状态
        /// </summary>
        void SetStates(IDictionary<string, ITensor> states);

        /// <summary>
        /// 重置层的内部状态（如序列起始时重置RNN状态）
        /// </summary>
        void ResetStates();
        #endregion

        #region 序列化与部署
        /// <summary>
        /// 保存层配置（不含参数）
        /// </summary>
        JsonNode SaveConfiguration();

        /// <summary>
        /// 加载层配置
        /// </summary>
        bool LoadConfiguration(JsonNode config);

        /// <summary>
        /// 导出参数（支持不同格式：JSON/二进制）
        /// </summary>
        byte[] ExportParameters(SerializationFormat format = SerializationFormat.Binary);

        /// <summary>
        /// 导入参数
        /// </summary>
        bool ImportParameters(byte[] data, SerializationFormat format = SerializationFormat.Binary);

        /// <summary>
        /// 转换为推理优化模式（移除训练相关参数，如dropout掩码）
        /// </summary>
        void OptimizeForInference();
        #endregion

        #region 调试与分析
        /// <summary>
        /// 获取层的计算图节点表示（用于可视化）
        /// </summary>
        GraphNode GetGraphNode();

        /// <summary>
        /// 获取前向传播的中间结果（仅调试模式）
        /// </summary>
        IDictionary<string, ITensor> GetDebugIntermediates();

        /// <summary>
        /// 计算输入对输出的梯度（用于特征重要性分析）
        /// </summary>
        ITensor ComputeInputGradient(ITensor input, int outputIndex = -1);
        #endregion

        #region 层连接管理
        /// <summary>
        /// 获取当前层的后续层（用于自动构建计算图）
        /// </summary>
        IReadOnlyList<ILayer> NextLayers { get; }

        /// <summary>
        /// 获取当前层的前置层
        /// </summary>
        IReadOnlyList<ILayer> PreviousLayers { get; }

        /// <summary>
        /// 添加后续层（用于构建复杂网络结构）
        /// </summary>
        void AddNextLayer(ILayer layer);

        /// <summary>
        /// 移除后续层
        /// </summary>
        void RemoveNextLayer(ILayer layer);
        #endregion

        #region 性能监控
        /// <summary>
        /// 获取前向传播的平均耗时（毫秒）
        /// </summary>
        double ForwardTimeMs { get; }

        /// <summary>
        /// 获取反向传播的平均耗时（毫秒）
        /// </summary>
        double BackwardTimeMs { get; }

        /// <summary>
        /// 重置性能统计
        /// </summary>
        void ResetPerformanceStats();
        #endregion
    }

    #region 辅助枚举与类型
    /// <summary>
    /// 计算精度模式
    /// </summary>
    public enum PrecisionMode
    {
        Float32,  // 单精度浮点
        Float16,  // 半精度浮点
        BFloat16, // 脑浮点
        Int8      // 8位整数（量化）
    }

    /// <summary>
    /// 量化模式
    /// </summary>
    public enum QuantizationMode
    {
        None,          // 不量化
        Dynamic,       // 动态量化
        Static,        // 静态量化
        PerChannel     // 按通道量化
    }

    /// <summary>
    /// 序列化格式
    /// </summary>
    public enum SerializationFormat
    {
        Json,    // 文本JSON格式
        Binary   // 二进制格式（更紧凑）
    }

    /// <summary>
    /// 计算图节点（用于可视化）
    /// </summary>
    public class GraphNode
    {
        public string LayerName { get; set; }
        public string LayerType { get; set; }
        public string InputShape { get; set; }
        public string OutputShape { get; set; }
        public long ParameterCount { get; set; }
        public List<GraphNode> Children { get; set; } = new List<GraphNode>();
    }
    #endregion
}