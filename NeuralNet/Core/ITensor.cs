using System;

namespace NeuralNetworkLibrary.Core
{
    /// <summary>
    /// 张量接口，代表神经网络中的多维数据
    /// </summary>
    public interface ITensor
    {
        int[] Shape { get; }
        float[] Data { get; }
        int Size { get; }
        
        float this[params int[] indices] { get; set; }
        ITensor Clone();
        ITensor CreateLike();
        void Fill(float value);
        void Randomize(float min, float max, Random random);
    }
}
