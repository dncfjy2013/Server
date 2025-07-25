using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Linq;

namespace NeuralNetworkLibrary.Core
{
    public class Tensor : ITensor
    {
        // 基础属性
        public int[] Shape { get; }
        public float[] Data { get; }
        public int Size { get; }
        public int Rank => Shape.Length;
        public int VectorLength => Vector<float>.Count;

        // 内部优化字段
        private readonly int[] _strides;
        private readonly bool _enableBoundsCheck;
        private readonly int _rank;

        // 线性索引器（直接访问底层数组）
        public float this[int linearIndex]
        {
            get
            {
                if (_enableBoundsCheck && (linearIndex < 0 || linearIndex >= Size))
                    throw new IndexOutOfRangeException($"线性索引{linearIndex}超出范围（0~{Size - 1}）");
                return Data[linearIndex];
            }
            set
            {
                if (_enableBoundsCheck && (linearIndex < 0 || linearIndex >= Size))
                    throw new IndexOutOfRangeException($"线性索引{linearIndex}超出范围（0~{Size - 1}）");
                Data[linearIndex] = value;
            }
        }

        // 多维索引器（复用原有实现）
        public float this[params int[] indices]
        {
            get => Data[CalculateLinearIndex(indices)];
            set => Data[CalculateLinearIndex(indices)] = value;
        }

        // 构造函数
        public Tensor(int[] shape, bool enableBoundsCheck = false)
        {
            Shape = (int[])shape.Clone();
            _rank = Shape.Length;
            Size = CalculateSize(Shape);
            Data = new float[Size];
            _strides = CalculateStrides(Shape);
            _enableBoundsCheck = enableBoundsCheck;
        }

        public Tensor(params int[] shape) : this(shape, enableBoundsCheck: false) { }

        // 基础工具方法
        private static int CalculateSize(int[] shape)
        {
            int size = 1;
            foreach (int dim in shape)
            {
                if (dim <= 0) throw new ArgumentException("张量维度必须为正数");
                size *= dim;
            }
            return size;
        }

        private static int[] CalculateStrides(int[] shape)
        {
            int rank = shape.Length;
            int[] strides = new int[rank];
            if (rank == 0) return strides;

            strides[rank - 1] = 1;
            for (int i = rank - 2; i >= 0; i--)
            {
                strides[i] = strides[i + 1] * shape[i + 1];
            }
            return strides;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int CalculateLinearIndex(int[] indices)
        {
            if (_enableBoundsCheck)
            {
                if (indices.Length != _rank)
                    throw new ArgumentException($"索引维度不匹配（预期{_rank}，实际{indices.Length}）");
                for (int i = 0; i < _rank; i++)
                {
                    if (indices[i] < 0 || indices[i] >= Shape[i])
                        throw new IndexOutOfRangeException($"维度{i}的索引{indices[i]}超出范围（0~{Shape[i] - 1}）");
                }
            }

            int linearIndex = 0;
            for (int i = 0; i < _rank; i++)
                linearIndex += indices[i] * _strides[i];
            return linearIndex;
        }


        #region 数据初始化
        public void RandomizeNormal(float mean = 0f, float stdDev = 1f, Random random = null)
        {
            random ??= new Random();
            float[] data = Data;
            int n = data.Length;
            int i = 0;

            // 使用Box-Muller变换生成正态分布随机数（批量处理提升效率）
            while (i < n)
            {
                // 生成两个独立的均匀分布随机数
                float u1 = (float)random.NextDouble();
                float u2 = (float)random.NextDouble();

                // 变换为两个标准正态分布随机数
                float z0 = (float)Math.Sqrt(-2 * Math.Log(u1)) * (float)Math.Cos(2 * Math.PI * u2);
                float z1 = (float)Math.Sqrt(-2 * Math.Log(u1)) * (float)Math.Sin(2 * Math.PI * u2);

                // 缩放并偏移到目标分布
                data[i] = z0 * stdDev + mean;
                i++;
                if (i < n)
                {
                    data[i] = z1 * stdDev + mean;
                    i++;
                }
            }
        }

        public ITensor Clone()
        {
            Tensor clone = new Tensor(Shape, _enableBoundsCheck);
            Array.Copy(Data, clone.Data, Size);
            return clone;
        }

        public ITensor CreateLike()
        {
            return new Tensor(Shape, _enableBoundsCheck);
        }

        public void Fill(float value)
        {
            int vectorLength = Vector<float>.Count;
            int vectorCount = Size / vectorLength;
            int remaining = Size % vectorLength;

            Vector<float> vValue = new Vector<float>(value);
            for (int i = 0; i < vectorCount; i++)
                vValue.CopyTo(Data, i * vectorLength);

            for (int i = vectorCount * vectorLength; i < Size; i++)
                Data[i] = value;
        }

        public void Randomize(float min, float max, Random random)
        {
            if (min > max) throw new ArgumentException("min不能大于max");
            float range = max - min;
            int vectorLength = Vector<float>.Count;
            int vectorCount = Size / vectorLength;
            int remaining = Size % vectorLength;

            Span<float> buffer = stackalloc float[vectorLength];
            for (int i = 0; i < vectorCount; i++)
            {
                for (int j = 0; j < vectorLength; j++)
                    buffer[j] = (float)random.NextDouble() * range + min;
                new Vector<float>(buffer).CopyTo(Data, i * vectorLength);
            }

            for (int i = vectorCount * vectorLength; i < Size; i++)
                Data[i] = (float)random.NextDouble() * range + min;
        }
        #endregion


        #region 元素级操作
        public void Add(ITensor other)
        {
            CheckShapeCompatibility(other.Shape, nameof(Add));
            if (other is Tensor tensor)
                AddSIMD(tensor);
            else
                ElementwiseOperation(other, (a, b) => a + b);
        }

        public void Add(float scalar)
        {
            int vectorLength = Vector<float>.Count;
            int vectorCount = Size / vectorLength;
            int remaining = Size % vectorLength;

            Vector<float> vScalar = new Vector<float>(scalar);
            for (int i = 0; i < vectorCount; i++)
            {
                int index = i * vectorLength;
                Vector<float> v = new Vector<float>(Data, index);
                (v + vScalar).CopyTo(Data, index);
            }

            for (int i = vectorCount * vectorLength; i < Size; i++)
                Data[i] += scalar;
        }

        public void Subtract(ITensor other)
        {
            CheckShapeCompatibility(other.Shape, nameof(Subtract));
            ElementwiseOperation(other, (a, b) => a - b);
        }

        public void Subtract(float scalar)
        {
            int vectorLength = Vector<float>.Count;
            int vectorCount = Size / vectorLength;
            int remaining = Size % vectorLength;

            Vector<float> vScalar = new Vector<float>(scalar);
            for (int i = 0; i < vectorCount; i++)
            {
                int index = i * vectorLength;
                Vector<float> v = new Vector<float>(Data, index);
                (v - vScalar).CopyTo(Data, index);
            }

            for (int i = vectorCount * vectorLength; i < Size; i++)
                Data[i] -= scalar;
        }

        public void Multiply(ITensor other)
        {
            CheckShapeCompatibility(other.Shape, nameof(Multiply));
            if (other is Tensor tensor)
                MultiplySIMD(tensor);
            else
                ElementwiseOperation(other, (a, b) => a * b);
        }

        public void Multiply(float scalar)
        {
            int vectorLength = Vector<float>.Count;
            int vectorCount = Size / vectorLength;
            int remaining = Size % vectorLength;

            Vector<float> vScalar = new Vector<float>(scalar);
            for (int i = 0; i < vectorCount; i++)
            {
                int index = i * vectorLength;
                Vector<float> v = new Vector<float>(Data, index);
                (v * vScalar).CopyTo(Data, index);
            }

            for (int i = vectorCount * vectorLength; i < Size; i++)
                Data[i] *= scalar;
        }

        public void Divide(ITensor other)
        {
            CheckShapeCompatibility(other.Shape, nameof(Divide));
            ElementwiseOperation(other, (a, b) => a / b);
        }

        public void Divide(float scalar)
        {
            if (scalar == 0) throw new DivideByZeroException("除数不能为0");
            float invScalar = 1f / scalar; // 避免重复除法
            Multiply(invScalar);
        }

        public void Square()
        {
            int vectorLength = Vector<float>.Count;
            int vectorCount = Size / vectorLength;
            int remaining = Size % vectorLength;

            for (int i = 0; i < vectorCount; i++)
            {
                int index = i * vectorLength;
                Vector<float> v = new Vector<float>(Data, index);
                (v * v).CopyTo(Data, index);
            }

            for (int i = vectorCount * vectorLength; i < Size; i++)
                Data[i] *= Data[i];
        }

        public void Sqrt()
        {
            ElementwiseOperation(a => (float)Math.Sqrt(a));
        }

        public void Exp()
        {
            ElementwiseOperation(a => (float)Math.Exp(a));
        }

        public void Log()
        {
            ElementwiseOperation(a => (float)Math.Log(a));
        }

        public void Abs()
        {
            int vectorLength = Vector<float>.Count;
            int vectorCount = Size / vectorLength;
            int remaining = Size % vectorLength;

            for (int i = 0; i < vectorCount; i++)
            {
                int index = i * vectorLength;
                Vector<float> v = new Vector<float>(Data, index);
                Vector.Abs(v).CopyTo(Data, index);
            }

            for (int i = vectorCount * vectorLength; i < Size; i++)
                Data[i] = Math.Abs(Data[i]);
        }

        public void Negate()
        {
            Multiply(-1f);
        }

        public void Max(float scalar)
        {
            ElementwiseOperation(a => Math.Max(a, scalar));
        }

        public void Max(ITensor other)
        {
            CheckShapeCompatibility(other.Shape, nameof(Max));
            ElementwiseOperation(other, (a, b) => Math.Max(a, b));
        }

        public void Min(float scalar)
        {
            ElementwiseOperation(a => Math.Min(a, scalar));
        }

        public void Min(ITensor other)
        {
            CheckShapeCompatibility(other.Shape, nameof(Min));
            ElementwiseOperation(other, (a, b) => Math.Min(a, b));
        }

        // 辅助方法：元素级操作（单张量）
        private void ElementwiseOperation(Func<float, float> op)
        {
            for (int i = 0; i < Size; i++)
                Data[i] = op(Data[i]);
        }

        // 辅助方法：元素级操作（双张量）
        private void ElementwiseOperation(ITensor other, Func<float, float, float> op)
        {
            for (int i = 0; i < Size; i++)
                Data[i] = op(Data[i], other.Data[i]);
        }

        // 检查形状兼容性
        private void CheckShapeCompatibility(int[] otherShape, string operation)
        {
            if (!Shape.SequenceEqual(otherShape))
                throw new ArgumentException($"{operation}操作要求张量形状匹配（当前：{ShapeToString(Shape)}，输入：{ShapeToString(otherShape)}）");
        }

        private string ShapeToString(int[] shape)
        {
            return $"({string.Join(", ", shape)})";
        }
        #endregion


        #region 归约操作
        public float Sum()
        {
            float sum = 0f;
            int vectorLength = Vector<float>.Count;
            int vectorCount = Size / vectorLength;
            int remaining = Size % vectorLength;

            Vector<float> vSum = Vector<float>.Zero;
            for (int i = 0; i < vectorCount; i++)
            {
                int index = i * vectorLength;
                vSum += new Vector<float>(Data, index);
            }
            sum = VectorSum(vSum);

            for (int i = vectorCount * vectorLength; i < Size; i++)
                sum += Data[i];

            return sum;
        }

        public ITensor Sum(int dimension, bool keepDims = false)
        {
            CheckDimensionValidity(dimension);
            int[] outputShape = GetReducedShape(dimension, keepDims);
            Tensor result = new Tensor(outputShape, _enableBoundsCheck);
            int reducedSize = Shape[dimension];
            int outerSize = 1;
            for (int i = 0; i < dimension; i++)
                outerSize *= Shape[i];
            int innerSize = 1;
            for (int i = dimension + 1; i < _rank; i++)
                innerSize *= Shape[i];

            Parallel.For(0, outerSize, o =>
            {
                for (int i = 0; i < innerSize; i++)
                {
                    float sum = 0f;
                    for (int r = 0; r < reducedSize; r++)
                    {
                        int[] indices = GetIndicesFromOuterInner(o, i, r, dimension, outerSize, innerSize);
                        sum += this[indices];
                    }
                    int[] resultIndices = keepDims ? InsertDimension(indices: GetIndicesFromOuterInner(o, i, 0, dimension, outerSize, innerSize), dimension, 0) : GetIndicesFromOuterInner(o, i, 0, dimension, outerSize, innerSize);
                    result[resultIndices] = sum;
                }
            });

            return result;
        }

        public float Mean()
        {
            return Sum() / Size;
        }

        public ITensor Mean(int dimension, bool keepDims = false)
        {
            ITensor sum = Sum(dimension, keepDims);
            sum.Divide(Shape[dimension]);
            return sum;
        }

        public float Max()
        {
            if (Size == 0) throw new InvalidOperationException("空张量无法计算最大值");
            float max = Data[0];
            for (int i = 1; i < Size; i++)
                if (Data[i] > max) max = Data[i];
            return max;
        }

        public ITensor Max(int dimension, bool keepDims = false)
        {
            CheckDimensionValidity(dimension);
            int[] outputShape = GetReducedShape(dimension, keepDims);
            Tensor result = new Tensor(outputShape, _enableBoundsCheck);
            int reducedSize = Shape[dimension];
            int outerSize = 1;
            for (int i = 0; i < dimension; i++)
                outerSize *= Shape[i];
            int innerSize = 1;
            for (int i = dimension + 1; i < _rank; i++)
                innerSize *= Shape[i];

            Parallel.For(0, outerSize, o =>
            {
                for (int i = 0; i < innerSize; i++)
                {
                    float max = float.MinValue;
                    for (int r = 0; r < reducedSize; r++)
                    {
                        int[] indices = GetIndicesFromOuterInner(o, i, r, dimension, outerSize, innerSize);
                        float val = this[indices];
                        if (val > max) max = val;
                    }
                    int[] resultIndices = keepDims ? InsertDimension(GetIndicesFromOuterInner(o, i, 0, dimension, outerSize, innerSize), dimension, 0) : GetIndicesFromOuterInner(o, i, 0, dimension, outerSize, innerSize);
                    result[resultIndices] = max;
                }
            });

            return result;
        }

        public float Min()
        {
            if (Size == 0) throw new InvalidOperationException("空张量无法计算最小值");
            float min = Data[0];
            for (int i = 1; i < Size; i++)
                if (Data[i] < min) min = Data[i];
            return min;
        }

        public ITensor Min(int dimension, bool keepDims = false)
        {
            CheckDimensionValidity(dimension);
            int[] outputShape = GetReducedShape(dimension, keepDims);
            Tensor result = new Tensor(outputShape, _enableBoundsCheck);
            int reducedSize = Shape[dimension];
            int outerSize = 1;
            for (int i = 0; i < dimension; i++)
                outerSize *= Shape[i];
            int innerSize = 1;
            for (int i = dimension + 1; i < _rank; i++)
                innerSize *= Shape[i];

            Parallel.For(0, outerSize, o =>
            {
                for (int i = 0; i < innerSize; i++)
                {
                    float min = float.MaxValue;
                    for (int r = 0; r < reducedSize; r++)
                    {
                        int[] indices = GetIndicesFromOuterInner(o, i, r, dimension, outerSize, innerSize);
                        float val = this[indices];
                        if (val < min) min = val;
                    }
                    int[] resultIndices = keepDims ? InsertDimension(GetIndicesFromOuterInner(o, i, 0, dimension, outerSize, innerSize), dimension, 0) : GetIndicesFromOuterInner(o, i, 0, dimension, outerSize, innerSize);
                    result[resultIndices] = min;
                }
            });

            return result;
        }

        public float SumOfSquares()
        {
            float sum = 0f;
            int vectorLength = Vector<float>.Count;
            int vectorCount = Size / vectorLength;
            int remaining = Size % vectorLength;

            Vector<float> vSum = Vector<float>.Zero;
            for (int i = 0; i < vectorCount; i++)
            {
                int index = i * vectorLength;
                Vector<float> v = new Vector<float>(Data, index);
                vSum += v * v;
            }
            sum = VectorSum(vSum);

            for (int i = vectorCount * vectorLength; i < Size; i++)
                sum += Data[i] * Data[i];

            return sum;
        }

        // 辅助方法：计算归约后的形状
        private int[] GetReducedShape(int dimension, bool keepDims)
        {
            if (keepDims)
            {
                int[] shape = (int[])Shape.Clone();
                shape[dimension] = 1;
                return shape;
            }
            return Shape.Where((_, i) => i != dimension).ToArray();
        }

        // 辅助方法：检查维度有效性
        private void CheckDimensionValidity(int dimension)
        {
            if (dimension < 0 || dimension >= _rank)
                throw new ArgumentOutOfRangeException(nameof(dimension), $"维度{dimension}超出范围（0~{_rank - 1}）");
        }

        // 辅助方法：向量求和
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float VectorSum(Vector<float> vector)
        {
            float sum = 0f;
            for (int i = 0; i < Vector<float>.Count; i++)
                sum += vector[i];
            return sum;
        }

        // 辅助方法：从outer、inner、reduced索引计算多维索引
        private int[] GetIndicesFromOuterInner(int outer, int inner, int reduced, int dimension, int outerSize, int innerSize)
        {
            int[] indices = new int[_rank];
            int remaining = outer;

            // 计算维度前的索引
            for (int i = 0; i < dimension; i++)
            {
                int dimSize = Shape[i];
                indices[i] = remaining % dimSize;
                remaining /= dimSize;
            }

            // 设置当前维度索引
            indices[dimension] = reduced;

            // 计算维度后的索引
            remaining = inner;
            for (int i = _rank - 1; i > dimension; i--)
            {
                int dimSize = Shape[i];
                indices[i] = remaining % dimSize;
                remaining /= dimSize;
            }

            return indices;
        }

        // 辅助方法：插入维度（用于keepDims）
        private int[] InsertDimension(int[] indices, int dimension, int value)
        {
            int[] newIndices = new int[indices.Length + 1];
            for (int i = 0; i < dimension; i++)
                newIndices[i] = indices[i];
            newIndices[dimension] = value;
            for (int i = dimension + 1; i < newIndices.Length; i++)
                newIndices[i] = indices[i - 1];
            return newIndices;
        }
        #endregion


        #region 形状操作
        public ITensor Reshape(params int[] newShape)
        {
            int newSize = newShape.Aggregate(1, (a, b) => a * b);
            if (newSize != Size)
                throw new ArgumentException($"重塑失败：新形状{ShapeToString(newShape)}的大小{newSize}与原大小{Size}不匹配");

            Tensor reshaped = new Tensor(newShape, _enableBoundsCheck);
            Array.Copy(Data, reshaped.Data, Size); // 数据顺序不变，仅改变形状
            return reshaped;
        }

        public ITensor Transpose(params int[] permutation)
        {
            if (permutation.Length != _rank)
                throw new ArgumentException($"排列长度{permutation.Length}必须与维度数量{_rank}匹配");
            if (permutation.Distinct().Count() != _rank)
                throw new ArgumentException("排列包含重复值");
            for (int i = 0; i < _rank; i++)
                if (permutation[i] < 0 || permutation[i] >= _rank)
                    throw new ArgumentException($"排列值{permutation[i]}超出范围（0~{_rank - 1}）");

            int[] newShape = permutation.Select(p => Shape[p]).ToArray();
            Tensor transposed = new Tensor(newShape, _enableBoundsCheck);

            // 计算新strides
            int[] newStrides = new int[_rank];
            for (int i = 0; i < _rank; i++)
                newStrides[i] = _strides[permutation[i]];

            // 复制数据（按新顺序）
            for (int i = 0; i < Size; i++)
            {
                int[] newIndices = IndexFromLinear(i, newShape, newStrides);
                int[] oldIndices = permutation.Select(p => newIndices[p]).ToArray();
                transposed[newIndices] = this[oldIndices];
            }

            return transposed;
        }

        public ITensor ExpandDims(int dimension)
        {
            CheckDimensionValidity(dimension);
            int[] newShape = InsertDimension(Shape, dimension, 1);
            return Reshape(newShape);
        }

        public ITensor Squeeze(int? dimension = null)
        {
            if (dimension.HasValue)
            {
                CheckDimensionValidity(dimension.Value);
                if (Shape[dimension.Value] != 1)
                    throw new InvalidOperationException($"无法压缩维度{dimension.Value}（大小为{Shape[dimension.Value]}，非1）");
                return Reshape(Shape.Where((_, i) => i != dimension.Value).ToArray());
            }
            // 压缩所有大小为1的维度
            return Reshape(Shape.Where(dim => dim != 1).ToArray());
        }

        public ITensor Concatenate(ITensor other, int dimension)
        {
            if (!(other is Tensor otherTensor))
                throw new ArgumentException("只能拼接Tensor类型");
            CheckDimensionValidity(dimension);

            // 检查除拼接维度外的其他维度是否匹配
            for (int i = 0; i < _rank; i++)
            {
                if (i != dimension && !Shape[i].Equals(otherTensor.Shape[i]))
                    throw new ArgumentException($"拼接维度不匹配：维度{i}（当前{Shape[i]}，输入{otherTensor.Shape[i]}）");
            }

            int[] newShape = (int[])Shape.Clone();
            newShape[dimension] += otherTensor.Shape[dimension];
            Tensor result = new Tensor(newShape, _enableBoundsCheck);

            // 复制当前张量数据
            int[] currentSliceShape = Shape.Clone() as int[];
            currentSliceShape[dimension] = 1;
            for (int i = 0; i < Shape[dimension]; i++)
            {
                int[] sliceIndices = Enumerable.Repeat(0, _rank).ToArray();
                sliceIndices[dimension] = i;
                int[] resultSliceIndices = (int[])sliceIndices.Clone();
                CopySliceTo(result, resultSliceIndices, sliceIndices, currentSliceShape);
            }

            // 复制其他张量数据
            for (int i = 0; i < otherTensor.Shape[dimension]; i++)
            {
                int[] sliceIndices = Enumerable.Repeat(0, _rank).ToArray();
                sliceIndices[dimension] = i;
                int[] resultSliceIndices = (int[])sliceIndices.Clone();
                resultSliceIndices[dimension] = i + Shape[dimension];
                otherTensor.CopySliceTo(result, resultSliceIndices, sliceIndices, currentSliceShape);
            }

            return result;
        }

        // 辅助方法：复制切片到目标张量
        private void CopySliceTo(Tensor target, int[] targetSliceIndices, int[] sourceSliceIndices, int[] sliceShape)
        {
            int sliceSize = sliceShape.Aggregate(1, (a, b) => a * b);
            int[] sourceStrides = _strides;
            int[] targetStrides = target._strides;

            int sourceBase = CalculateLinearIndex(sourceSliceIndices);
            int targetBase = target.CalculateLinearIndex(targetSliceIndices);

            // 按切片形状复制数据
            for (int i = 0; i < sliceSize; i++)
            {
                int[] offsetIndices = IndexFromLinear(i, sliceShape, CalculateStrides(sliceShape));
                int sourceOffset = 0;
                int targetOffset = 0;
                for (int d = 0; d < _rank; d++)
                {
                    sourceOffset += offsetIndices[d] * sourceStrides[d];
                    targetOffset += offsetIndices[d] * targetStrides[d];
                }
                target.Data[targetBase + targetOffset] = Data[sourceBase + sourceOffset];
            }
        }

        // 辅助方法：从线性索引计算多维索引
        private int[] IndexFromLinear(int linearIndex, int[] shape, int[] strides)
        {
            int rank = shape.Length;
            int[] indices = new int[rank];
            int remaining = linearIndex;
            for (int i = 0; i < rank; i++)
            {
                indices[i] = remaining / strides[i];
                remaining %= strides[i];
            }
            return indices;
        }
        #endregion


        #region 广播操作
        public bool IsBroadcastableTo(int[] targetShape)
        {
            int targetRank = targetShape.Length;
            int maxRank = Math.Max(_rank, targetRank);

            for (int i = 0; i < maxRank; i++)
            {
                int currentDim = i < _rank ? Shape[i] : 1;
                int targetDim = i < targetRank ? targetShape[i] : 1;

                if (currentDim != targetDim && currentDim != 1 && targetDim != 1)
                    return false;
            }
            return true;
        }

        public ITensor BroadcastTo(int[] targetShape)
        {
            if (!IsBroadcastableTo(targetShape))
                throw new InvalidOperationException($"无法广播形状{ShapeToString(Shape)}到{ShapeToString(targetShape)}");

            Tensor broadcasted = new Tensor(targetShape, _enableBoundsCheck);
            int[] broadcastStrides = CalculateStrides(targetShape);
            int[] sourceStrides = _strides;

            // 扩展原形状到目标rank（补1）
            int[] expandedShape = _rank < targetShape.Length
                ? Enumerable.Repeat(1, targetShape.Length - _rank).Concat(Shape).ToArray()
                : (int[])Shape.Clone();

            // 复制数据（广播规则：维度为1的维度重复数据）
            for (int i = 0; i < broadcasted.Size; i++)
            {
                int[] targetIndices = IndexFromLinear(i, targetShape, broadcastStrides);
                int[] sourceIndices = targetIndices.Select((idx, d) => expandedShape[d] == 1 ? 0 : idx).ToArray();
                // 裁剪sourceIndices到原rank（如果目标rank更大）
                if (_rank < targetShape.Length)
                    sourceIndices = sourceIndices.Skip(targetShape.Length - _rank).ToArray();
                broadcasted.Data[i] = this[sourceIndices];
            }

            return broadcasted;
        }
        #endregion


        #region 数据转换
        public void CopyTo(float[] destination)
        {
            if (destination.Length < Size)
                throw new ArgumentException($"目标数组长度{destination.Length}小于张量大小{Size}");
            Array.Copy(Data, destination, Size);
        }

        public void CopyFrom(float[] source)
        {
            if (source.Length < Size)
                throw new ArgumentException($"源数组长度{source.Length}小于张量大小{Size}");
            Array.Copy(source, Data, Size);
        }

        public float[,] To2DArray()
        {
            if (_rank != 2)
                throw new InvalidOperationException($"无法将{_rank}维张量转换为2D数组");

            int rows = Shape[0];
            int cols = Shape[1];
            float[,] arr = new float[rows, cols];
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                    arr[i, j] = this[i, j];
            return arr;
        }

        public float[,,] To3DArray()
        {
            if (_rank != 3)
                throw new InvalidOperationException($"无法将{_rank}维张量转换为3D数组");

            int dim0 = Shape[0];
            int dim1 = Shape[1];
            int dim2 = Shape[2];
            float[,,] arr = new float[dim0, dim1, dim2];
            for (int i = 0; i < dim0; i++)
                for (int j = 0; j < dim1; j++)
                    for (int k = 0; k < dim2; k++)
                        arr[i, j, k] = this[i, j, k];
            return arr;
        }
        #endregion


        #region SIMD加速实现
        public void AddSIMD(ITensor other)
        {
            if (!(other is Tensor tensor))
                throw new ArgumentException("仅支持Tensor类型的SIMD操作");

            int vectorLength = Vector<float>.Count;
            int vectorCount = Size / vectorLength;
            int remaining = Size % vectorLength;

            for (int i = 0; i < vectorCount; i++)
            {
                int index = i * vectorLength;
                Vector<float> v1 = new Vector<float>(Data, index);
                Vector<float> v2 = new Vector<float>(tensor.Data, index);
                (v1 + v2).CopyTo(Data, index);
            }

            for (int i = vectorCount * vectorLength; i < Size; i++)
                Data[i] += tensor.Data[i];
        }

        private void MultiplySIMD(Tensor other)
        {
            int vectorLength = Vector<float>.Count;
            int vectorCount = Size / vectorLength;
            int remaining = Size % vectorLength;

            for (int i = 0; i < vectorCount; i++)
            {
                int index = i * vectorLength;
                Vector<float> v1 = new Vector<float>(Data, index);
                Vector<float> v2 = new Vector<float>(other.Data, index);
                (v1 * v2).CopyTo(Data, index);
            }

            for (int i = vectorCount * vectorLength; i < Size; i++)
                Data[i] *= other.Data[i];
        }
        #endregion

        /// <summary>
        /// 实现元素级自定义操作（利用SIMD加速）
        /// </summary>
        public void Apply(Func<float, float> operation)
        {
            int vectorLength = Vector<float>.Count;
            int vectorCount = Size / vectorLength;
            int remaining = Size % vectorLength;

            // 对支持SIMD的部分批量处理
            for (int i = 0; i < vectorCount; i++)
            {
                int index = i * vectorLength;
                // 读取向量
                Vector<float> v = new Vector<float>(Data, index);
                // 对每个元素应用操作（通过Span实现高效处理）
                Span<float> span = new Span<float>(Data, index, vectorLength);
                for (int j = 0; j < vectorLength; j++)
                {
                    span[j] = operation(span[j]);
                }
            }

            // 处理剩余元素
            for (int i = vectorCount * vectorLength; i < Size; i++)
            {
                Data[i] = operation(Data[i]);
            }
        }

        /// <summary>
        /// 克隆张量并对元素应用自定义操作
        /// </summary>
        public ITensor ApplyAndClone(Func<float, float> operation)
        {
            ITensor clone = Clone();
            clone.Apply(operation);
            return clone;
        }
    }
}