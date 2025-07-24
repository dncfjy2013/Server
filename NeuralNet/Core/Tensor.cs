using System;

namespace NeuralNetworkLibrary.Core
{
    /// <summary>
    /// 张量的基本实现
    /// </summary>
    public class Tensor : ITensor
    {
        public int[] Shape { get; }
        public float[] Data { get; }
        public int Size { get; }
        
        private int[] _strides;

        public Tensor(params int[] shape)
        {
            Shape = shape;
            Size = CalculateSize(shape);
            Data = new float[Size];
            CalculateStrides();
        }

        private int CalculateSize(int[] shape)
        {
            int size = 1;
            foreach (int dim in shape)
            {
                size *= dim;
            }
            return size;
        }

        private void CalculateStrides()
        {
            _strides = new int[Shape.Length];
            _strides[Shape.Length - 1] = 1;
            
            for (int i = Shape.Length - 2; i >= 0; i--)
            {
                _strides[i] = _strides[i + 1] * Shape[i + 1];
            }
        }

        public float this[params int[] indices]
        {
            get
            {
                CheckIndices(indices);
                return Data[CalculateIndex(indices)];
            }
            set
            {
                CheckIndices(indices);
                Data[CalculateIndex(indices)] = value;
            }
        }

        private int CalculateIndex(int[] indices)
        {
            int index = 0;
            for (int i = 0; i < indices.Length; i++)
            {
                index += indices[i] * _strides[i];
            }
            return index;
        }

        private void CheckIndices(int[] indices)
        {
            if (indices.Length != Shape.Length)
                throw new ArgumentException("Indices length does not match tensor rank");

            for (int i = 0; i < indices.Length; i++)
            {
                if (indices[i] < 0 || indices[i] >= Shape[i])
                    throw new IndexOutOfRangeException($"Index {indices[i]} is out of range for dimension {i}");
            }
        }

        public ITensor Clone()
        {
            var clone = new Tensor(Shape);
            Array.Copy(Data, clone.Data, Data.Length);
            return clone;
        }

        public ITensor CreateLike()
        {
            return new Tensor(Shape);
        }

        public void Fill(float value)
        {
            Array.Fill(Data, value);
        }

        public void Randomize(float min, float max, Random random)
        {
            for (int i = 0; i < Data.Length; i++)
            {
                Data[i] = (float)(random.NextDouble() * (max - min) + min);
            }
        }
    }
}
