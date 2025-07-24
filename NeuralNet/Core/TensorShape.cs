namespace NeuralNetworkLibrary.Core
{
    /// <summary>
    /// 封装张量的形状信息
    /// </summary>
    public class TensorShape
    {
        public int[] Dimensions { get; }
        
        public TensorShape(params int[] dimensions)
        {
            Dimensions = dimensions;
        }
        
        public int this[int index] => Dimensions[index];
        public int Rank => Dimensions.Length;
        
        public int Size
        {
            get
            {
                int size = 1;
                foreach (int dim in Dimensions)
                    size *= dim;
                return size;
            }
        }
        
        public override string ToString()
        {
            return $"({string.Join(", ", Dimensions)})";
        }
        
        public TensorShape Clone()
        {
            return new TensorShape((int[])Dimensions.Clone());
        }
    }
}
