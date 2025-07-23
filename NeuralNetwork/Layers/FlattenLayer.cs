using System;

namespace NeuralNetwork.Layers
{
    /// <summary>
    /// 扁平化层，将多维数组转换为一维数组
    /// </summary>
    public class FlattenLayer : ILayer
    {
        private int _inputDepth;
        private int _inputHeight;
        private int _inputWidth;
        
        public string Name { get; set; } = "Flatten";
        public int ParameterCount => 0;  // 扁平化层没有可训练参数

        /// <summary>
        /// 前向传播 - 将多维数组扁平化为一维数组
        /// </summary>
        public float[,,] Forward(float[,,] input)
        {
            _inputDepth = input.GetLength(0);
            _inputHeight = input.GetLength(1);
            _inputWidth = input.GetLength(2);
            
            int outputSize = _inputDepth * _inputHeight * _inputWidth;
            float[,,] output = new float[outputSize, 1, 1];
            
            int index = 0;
            for (int d = 0; d < _inputDepth; d++)
            {
                for (int h = 0; h < _inputHeight; h++)
                {
                    for (int w = 0; w < _inputWidth; w++)
                    {
                        output[index++, 0, 0] = input[d, h, w];
                    }
                }
            }
            
            return output;
        }
        
        /// <summary>
        /// 反向传播 - 将一维数组恢复为原始多维形状
        /// </summary>
        public float[,,] Backward(float[,,] outputGradient, float learningRate)
        {
            float[,,] inputGradient = new float[_inputDepth, _inputHeight, _inputWidth];
            
            int index = 0;
            for (int d = 0; d < _inputDepth; d++)
            {
                for (int h = 0; h < _inputHeight; h++)
                {
                    for (int w = 0; w < _inputWidth; w++)
                    {
                        inputGradient[d, h, w] = outputGradient[index++, 0, 0];
                    }
                }
            }
            
            return inputGradient;
        }
    }
}
