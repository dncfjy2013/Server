using System;
using System.Numerics;

namespace NeuralNetworkLibrary.Core
{
    /// <summary>
    /// 扩展的张量接口，支持神经网络计算所需的核心操作
    /// </summary>
    public interface ITensor
    {
        #region 基础属性
        int[] Shape { get; }
        int Size { get; }
        int Rank { get; }  // 维度数量，等价于Shape.Length
        float[] Data { get; }
        #endregion

        #region 索引访问
        float this[params int[] indices] { get; set; }

        /// <summary>
        /// 通过线性索引访问元素（跳过多维到一维的转换）
        /// </summary>
        float this[int linearIndex] { get; set; }
        #endregion

        #region 数据初始化
        ITensor Clone();
        ITensor CreateLike();
        void Fill(float value);
        void Randomize(float min, float max, Random random);

        /// <summary>
        /// 用正态分布随机初始化
        /// </summary>
        void RandomizeNormal(float mean = 0f, float stdDev = 1f, Random random = null);
        #endregion

        #region 元素级操作
        /// <summary>
        /// 元素级加法（this = this + other）
        /// </summary>
        void Add(ITensor other);

        /// <summary>
        /// 元素级加法（this = this + scalar）
        /// </summary>
        void Add(float scalar);

        /// <summary>
        /// 元素级减法（this = this - other）
        /// </summary>
        void Subtract(ITensor other);

        /// <summary>
        /// 元素级减法（this = this - scalar）
        /// </summary>
        void Subtract(float scalar);

        /// <summary>
        /// 元素级乘法（this = this * other）
        /// </summary>
        void Multiply(ITensor other);

        /// <summary>
        /// 元素级乘法（this = this * scalar）
        /// </summary>
        void Multiply(float scalar);

        /// <summary>
        /// 元素级除法（this = this / other）
        /// </summary>
        void Divide(ITensor other);

        /// <summary>
        /// 元素级除法（this = this / scalar）
        /// </summary>
        void Divide(float scalar);

        /// <summary>
        /// 元素级平方（this = this²）
        /// </summary>
        void Square();

        /// <summary>
        /// 元素级开方（this = √this）
        /// </summary>
        void Sqrt();

        /// <summary>
        /// 元素级指数（this = e^this）
        /// </summary>
        void Exp();

        /// <summary>
        /// 元素级对数（this = ln(this)）
        /// </summary>
        void Log();

        /// <summary>
        /// 元素级绝对值（this = |this|）
        /// </summary>
        void Abs();

        /// <summary>
        /// 元素级取反（this = -this）
        /// </summary>
        void Negate();

        /// <summary>
        /// 元素级最大值（this = max(this, scalar)）
        /// </summary>
        void Max(float scalar);

        /// <summary>
        /// 元素级最大值（this = max(this, other)）
        /// </summary>
        void Max(ITensor other);

        /// <summary>
        /// 元素级最小值（this = min(this, scalar)）
        /// </summary>
        void Min(float scalar);

        /// <summary>
        /// 元素级最小值（this = min(this, other)）
        /// </summary>
        void Min(ITensor other);
        #endregion

        #region 归约操作
        /// <summary>
        /// 计算所有元素的和
        /// </summary>
        float Sum();

        /// <summary>
        /// 计算指定维度上的元素和
        /// </summary>
        ITensor Sum(int dimension, bool keepDims = false);

        /// <summary>
        /// 计算所有元素的平均值
        /// </summary>
        float Mean();

        /// <summary>
        /// 计算指定维度上的元素平均值
        /// </summary>
        ITensor Mean(int dimension, bool keepDims = false);

        /// <summary>
        /// 计算所有元素的最大值
        /// </summary>
        float Max();

        /// <summary>
        /// 计算指定维度上的元素最大值
        /// </summary>
        ITensor Max(int dimension, bool keepDims = false);

        /// <summary>
        /// 计算所有元素的最小值
        /// </summary>
        float Min();

        /// <summary>
        /// 计算指定维度上的元素最小值
        /// </summary>
        ITensor Min(int dimension, bool keepDims = false);

        /// <summary>
        /// 计算所有元素的平方和
        /// </summary>
        float SumOfSquares();
        #endregion

        #region 形状操作
        /// <summary>
        /// 重塑张量形状（元素总数必须保持不变）
        /// </summary>
        ITensor Reshape(params int[] newShape);

        /// <summary>
        /// 转置张量（交换维度顺序）
        /// </summary>
        ITensor Transpose(params int[] permutation);

        /// <summary>
        /// 对指定维度进行扩展（增加维度大小为1）
        /// </summary>
        ITensor ExpandDims(int dimension);

        /// <summary>
        /// 移除大小为1的维度
        /// </summary>
        ITensor Squeeze(int? dimension = null);

        /// <summary>
        /// 在指定维度上进行拼接
        /// </summary>
        ITensor Concatenate(ITensor other, int dimension);
        #endregion

        #region 广播操作
        /// <summary>
        /// 检查当前张量是否可广播到目标形状
        /// </summary>
        bool IsBroadcastableTo(int[] targetShape);

        /// <summary>
        /// 将张量广播到目标形状（返回新张量，原张量不变）
        /// </summary>
        ITensor BroadcastTo(int[] targetShape);
        #endregion

        #region 数据转换
        /// <summary>
        /// 将张量数据复制到目标数组
        /// </summary>
        void CopyTo(float[] destination);

        /// <summary>
        /// 从源数组复制数据到张量
        /// </summary>
        void CopyFrom(float[] source);

        /// <summary>
        /// 将张量数据转换为二维数组（适用于2D张量）
        /// </summary>
        float[,] To2DArray();

        /// <summary>
        /// 将张量数据转换为三维数组（适用于3D张量）
        /// </summary>
        float[,,] To3DArray();
        #endregion

        #region SIMD加速支持
        /// <summary>
        /// 获取支持的SIMD向量长度（用于底层优化）
        /// </summary>
        int VectorLength { get; }

        /// <summary>
        /// 元素级加法的SIMD加速实现（内部使用）
        /// </summary>
        void AddSIMD(ITensor other);
        #endregion
    }
}