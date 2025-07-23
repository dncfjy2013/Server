using System;

namespace NeuralNetwork
{
    /// <summary>
    /// 激活函数工具类
    /// </summary>
    public static class ActivationFunctions
    {
        /// <summary>
        /// 应用激活函数
        /// </summary>
        public static float[,,] ApplyActivation(float[,,] input, ActivationType type)
        {
            int depth = input.GetLength(0);
            int height = input.GetLength(1);
            int width = input.GetLength(2);
            float[,,] output = new float[depth, height, width];

            for (int d = 0; d < depth; d++)
            {
                for (int h = 0; h < height; h++)
                {
                    for (int w = 0; w < width; w++)
                    {
                        output[d, h, w] = ApplyActivation(input[d, h, w], type);
                    }
                }
            }

            return output;
        }

        /// <summary>
        /// 应用激活函数到单个值
        /// </summary>
        public static float ApplyActivation(float value, ActivationType type)
        {
            return type switch
            {
                ActivationType.ReLU => ReLU(value),
                ActivationType.Sigmoid => Sigmoid(value),
                ActivationType.Tanh => Tanh(value),
                ActivationType.Softmax => throw new InvalidOperationException("Softmax should be applied to the entire layer"),
                ActivationType.LeakyReLU => LeakyReLU(value),
                _ => value
            };
        }

        /// <summary>
        /// 计算激活函数的导数
        /// </summary>
        public static float[,,] ApplyActivationDerivative(float[,,] input, ActivationType type)
        {
            int depth = input.GetLength(0);
            int height = input.GetLength(1);
            int width = input.GetLength(2);
            float[,,] output = new float[depth, height, width];

            for (int d = 0; d < depth; d++)
            {
                for (int h = 0; h < height; h++)
                {
                    for (int w = 0; w < width; w++)
                    {
                        output[d, h, w] = ApplyActivationDerivative(input[d, h, w], type);
                    }
                }
            }

            return output;
        }

        /// <summary>
        /// 计算单个值的激活函数导数
        /// </summary>
        public static float ApplyActivationDerivative(float value, ActivationType type)
        {
            return type switch
            {
                ActivationType.ReLU => ReLUDerivative(value),
                ActivationType.Sigmoid => SigmoidDerivative(value),
                ActivationType.Tanh => TanhDerivative(value),
                ActivationType.Softmax => 1.0f, // 通常与交叉熵损失结合使用时简化处理
                ActivationType.LeakyReLU => LeakyReLUDerivative(value),
                _ => 1.0f
            };
        }

        /// <summary>
        /// ReLU激活函数
        /// </summary>
        private static float ReLU(float x) => Math.Max(0, x);

        /// <summary>
        /// ReLU导数
        /// </summary>
        private static float ReLUDerivative(float x) => x > 0 ? 1 : 0;

        /// <summary>
        /// Leaky ReLU激活函数
        /// </summary>
        private static float LeakyReLU(float x) => x > 0 ? x : 0.01f * x;

        /// <summary>
        /// Leaky ReLU导数
        /// </summary>
        private static float LeakyReLUDerivative(float x) => x > 0 ? 1 : 0.01f;

        /// <summary>
        /// Sigmoid激活函数
        /// </summary>
        private static float Sigmoid(float x) => 1.0f / (1.0f + (float)Math.Exp(-x));

        /// <summary>
        /// Sigmoid导数
        /// </summary>
        private static float SigmoidDerivative(float x)
        {
            float sigmoid = Sigmoid(x);
            return sigmoid * (1 - sigmoid);
        }

        /// <summary>
        /// Tanh激活函数
        /// </summary>
        private static float Tanh(float x) => (float)Math.Tanh(x);

        /// <summary>
        /// Tanh导数
        /// </summary>
        private static float TanhDerivative(float x)
        {
            float tanh = Tanh(x);
            return 1 - tanh * tanh;
        }

        /// <summary>
        /// Softmax激活函数
        /// </summary>
        public static float[,,] Softmax(float[,,] input)
        {
            int depth = input.GetLength(0);
            int height = input.GetLength(1);
            int width = input.GetLength(2);
            float[,,] output = new float[depth, height, width];

            // 假设输入是单维度的特征向量
            if (height == 1 && width == 1)
            {
                float maxVal = input[0, 0, 0];
                for (int d = 0; d < depth; d++)
                {
                    if (input[d, 0, 0] > maxVal)
                        maxVal = input[d, 0, 0];
                }

                float sum = 0;
                for (int d = 0; d < depth; d++)
                {
                    output[d, 0, 0] = (float)Math.Exp(input[d, 0, 0] - maxVal);
                    sum += output[d, 0, 0];
                }

                for (int d = 0; d < depth; d++)
                {
                    output[d, 0, 0] /= sum;
                }
            }
            else
            {
                throw new NotImplementedException("Softmax is only implemented for 1D feature vectors");
            }

            return output;
        }
    }
}
