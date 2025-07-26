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
        float Version { get; }       // 版本号（用于兼容性处理）
        IDictionary<string, string> Metadata { get; }  // 附加元数据（如作者、描述）
        #endregion

        #region 计算模式与设备
        string Device { get; }  // 运行设备（"cpu"、"gpu:0"等）
        PrecisionMode Precision { get; set; }  // 计算精度（FP32/FP16/INT8）
        QuantizationMode Quantization { get; set; }  // 量化模式（仅推理）

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
        /// 对参数施加约束（如L2正则化、权重裁剪）
        /// </summary>
        void ApplyConstraints();

        /// <summary>
        /// 重置参数为初始状态
        /// </summary>
        void ResetParameters(Random random);
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

    #endregion
}