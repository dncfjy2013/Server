using NeuralNetworkLibrary.Core;
using NeuralNetworkLibrary.Optimizers;
using System;
using System.Text.Json.Nodes;

namespace NeuralNetworkLibrary.Layers
{
    /// <summary>
    /// 展平层，用于将多维张量转换为一维向量，通常用于卷积层和全连接层之间
    /// </summary>
    public class FlattenLayer : BaseLayer
    {
        public FlattenLayer(string name = "Flatten") : base(name) { }

        public override void SetInputShape(TensorShape inputShape)
        {
            InputShape = inputShape;
            
            // 计算展平后的大小
            int flattenedSize = 1;
            foreach (int dim in inputShape.Dimensions)
            {
                flattenedSize *= dim;
            }
            
            OutputShape = new TensorShape(flattenedSize);
        }

        public override void Initialize(Random random)
        {
            // 展平层没有参数需要初始化
        }

        public override ITensor Forward(ITensor input, bool isTraining = true)
        {
            var output = new Tensor(OutputShape.Dimensions);
            
            // 简单地复制数据（因为Tensor内部已经是扁平化存储）
            Array.Copy(input.Data, output.Data, input.Data.Length);
            
            return output;
        }

        public override ITensor Backward(ITensor gradient, float learningRate)
        {
            var inputGradient = new Tensor(InputShape.Dimensions);
            
            // 反向传播时同样只是复制数据
            Array.Copy(gradient.Data, inputGradient.Data, gradient.Data.Length);
            
            return inputGradient;
        }

        public override void UpdateParameters(IOptimizer optimizer)
        {

        }

        /// <summary>
        /// 序列化展平层配置信息
        /// </summary>
        public override JsonArray GetParameters()
        {
            JsonArray parameters = new JsonArray();

            // 1. 层配置信息
            JsonObject layerConfig = new JsonObject
            {
                ["type"] = "FlattenLayer",
                ["name"] = Name,
            };
            parameters.Add(layerConfig);

            // 2. 无参数，标记为null（保持与其他层结构一致）
            parameters.Add(null);

            return parameters;
        }

        /// <summary>
        /// 反序列化展平层配置信息
        /// </summary>
        public override bool LoadParameters(JsonArray param)
        {
            try
            {
                // 验证参数结构完整性
                if (param.Count != 2)
                    return false;

                // 验证层配置信息
                JsonObject layerConfig = param[0] as JsonObject;
                if (layerConfig == null ||
                    layerConfig["type"]?.ToString() != "FlattenLayer" ||
                    layerConfig["name"]?.ToString() != Name)
                    return false;

                // 验证无参数部分
                if (param[1] != null)
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
