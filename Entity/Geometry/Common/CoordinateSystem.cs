using System;
using System.Numerics;

namespace Entity.Geometry.Common
{
    /// <summary>
    /// 表示三维空间中的自定义坐标系，支持坐标转换、旋转、平移等几何操作
    /// 该类采用不可变设计，所有操作均返回新的坐标系实例
    /// </summary>
    public sealed class CoordinateSystem
    {
        /// <summary>坐标系原点（世界空间坐标）</summary>
        private readonly Vector3 _origin;

        /// <summary>X轴单位方向向量（局部到世界的变换基向量）</summary>
        private readonly Vector3 _xAxis;

        /// <summary>Y轴单位方向向量（局部到世界的变换基向量）</summary>
        private readonly Vector3 _yAxis;

        /// <summary>Z轴单位方向向量（局部到世界的变换基向量）</summary>
        private readonly Vector3 _zAxis;

        /// <summary>局部坐标到世界坐标的变换矩阵</summary>
        private readonly Matrix4x4 _transformationMatrix;

        /// <summary>世界坐标到局部坐标的逆变换矩阵</summary>
        private readonly Matrix4x4 _inverseTransformationMatrix;

        /// <summary>仅包含旋转部分的变换矩阵（不包含平移）</summary>
        private readonly Matrix4x4 _rotationMatrix;

        /// <summary>坐标系原点（世界空间坐标），只读</summary>
        public Vector3 Origin => _origin;

        /// <summary>X轴单位方向向量（世界空间），只读</summary>
        public Vector3 XAxis => _xAxis;

        /// <summary>Y轴单位方向向量（世界空间），只读</summary>
        public Vector3 YAxis => _yAxis;

        /// <summary>Z轴单位方向向量（世界空间），只读</summary>
        public Vector3 ZAxis => _zAxis;

        /// <summary>局部到世界的完整变换矩阵（包含旋转和平移），只读</summary>
        public Matrix4x4 TransformationMatrix => _transformationMatrix;

        /// <summary>世界到局部的逆变换矩阵，只读</summary>
        public Matrix4x4 InverseTransformationMatrix => _inverseTransformationMatrix;

        /// <summary>仅旋转部分的变换矩阵（不包含平移），只读</summary>
        public Matrix4x4 RotationMatrix => _rotationMatrix;

        /// <summary>
        /// 初始化一个默认的坐标系（世界坐标系）
        /// 原点在(0,0,0)，轴对齐世界坐标系
        /// </summary>
        public CoordinateSystem()
            : this(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ)
        {
        }

        /// <summary>
        /// 使用指定的原点和基向量初始化坐标系
        /// </summary>
        /// <param name="origin">坐标系原点（世界空间坐标）</param>
        /// <param name="xAxis">X轴方向向量（非零）</param>
        /// <param name="yAxis">Y轴方向向量（非零）</param>
        /// <param name="zAxis">Z轴方向向量（非零）</param>
        /// <exception cref="ArgumentException">当任一轴向量为零或三轴共面时抛出</exception>
        public CoordinateSystem(Vector3 origin, Vector3 xAxis, Vector3 yAxis, Vector3 zAxis)
        {
            if (xAxis == Vector3.Zero || yAxis == Vector3.Zero || zAxis == Vector3.Zero)
                throw new ArgumentException("坐标轴向量不能为零向量");

            _origin = origin;
            _xAxis = Vector3.Normalize(xAxis);
            _yAxis = Vector3.Normalize(yAxis);
            _zAxis = Vector3.Normalize(zAxis);

            if (IsCoplanar(_xAxis, _yAxis, _zAxis))
                throw new ArgumentException("三个基向量共面，无法构成有效的三维坐标系");

            (_transformationMatrix, _inverseTransformationMatrix) = CreateTransformationMatrices();
            _rotationMatrix = CreateRotationMatrix();
        }

        /// <summary>
        /// 使用指定原点和绕任意轴的旋转初始化坐标系
        /// </summary>
        /// <param name="origin">坐标系原点</param>
        /// <param name="rotationAxis">旋转轴（世界空间向量，将被归一化）</param>
        /// <param name="rotationAngle">旋转角度（弧度）</param>
        /// <exception cref="ArgumentException">当旋转轴为零或三轴共面时抛出</exception>
        public CoordinateSystem(Vector3 origin, Vector3 rotationAxis, float rotationAngle)
        {
            rotationAxis = Vector3.Normalize(rotationAxis);

            // 创建绕指定轴旋转的旋转矩阵
            Matrix4x4 rotationMatrix = Matrix4x4.CreateFromAxisAngle(rotationAxis, rotationAngle);

            // 使用单位向量乘以旋转矩阵来计算新坐标系的基向量
            _xAxis = Vector3.Transform(Vector3.UnitX, rotationMatrix);
            _yAxis = Vector3.Transform(Vector3.UnitY, rotationMatrix);
            _zAxis = Vector3.Transform(Vector3.UnitZ, rotationMatrix);

            _origin = origin;
            _xAxis = Vector3.Normalize(_xAxis);
            _yAxis = Vector3.Normalize(_yAxis);
            _zAxis = Vector3.Normalize(_zAxis);

            if (IsCoplanar(_xAxis, _yAxis, _zAxis))
                throw new ArgumentException("三个基向量共面，无法构成有效的三维坐标系");

            (_transformationMatrix, _inverseTransformationMatrix) = CreateTransformationMatrices();
            _rotationMatrix = CreateRotationMatrix();
        }

        /// <summary>
        /// 使用欧拉角和指定旋转顺序初始化坐标系
        /// </summary>
        /// <param name="origin">坐标系原点</param>
        /// <param name="xRotation">绕X轴旋转角度（弧度）</param>
        /// <param name="yRotation">绕Y轴旋转角度（弧度）</param>
        /// <param name="zRotation">绕Z轴旋转角度（弧度）</param>
        /// <param name="order">旋转顺序（默认ZYX顺序）</param>
        /// <exception cref="ArgumentException">当旋转产生的基向量共面时抛出</exception>
        public CoordinateSystem(Vector3 origin, float xRotation, float yRotation, float zRotation, RotationOrder order = RotationOrder.ZYX)
        {
            _origin = origin;

            // 创建三个轴的旋转矩阵
            Matrix4x4 xMatrix = CreateAxisRotationMatrix(Vector3.UnitX, xRotation);
            Matrix4x4 yMatrix = CreateAxisRotationMatrix(Vector3.UnitY, yRotation);
            Matrix4x4 zMatrix = CreateAxisRotationMatrix(Vector3.UnitZ, zRotation);

            // 按指定顺序组合旋转矩阵
            Matrix4x4 combinedRotation = CombineRotationMatrices(xMatrix, yMatrix, zMatrix, order);

            // 使用单位向量通过组合旋转矩阵计算新坐标系的基向量
            _xAxis = Vector3.Transform(Vector3.UnitX, combinedRotation);
            _yAxis = Vector3.Transform(Vector3.UnitY, combinedRotation);
            _zAxis = Vector3.Transform(Vector3.UnitZ, combinedRotation);

            // 归一化基向量
            _xAxis = Vector3.Normalize(_xAxis);
            _yAxis = Vector3.Normalize(_yAxis);
            _zAxis = Vector3.Normalize(_zAxis);

            // 验证三个基向量是否正交
            if (IsCoplanar(_xAxis, _yAxis, _zAxis))
                throw new ArgumentException("旋转产生的基向量共面，无法构成有效的三维坐标系");

            // 创建变换矩阵和逆变换矩阵
            (_transformationMatrix, _inverseTransformationMatrix) = CreateTransformationMatrices();
            _rotationMatrix = combinedRotation;
        }

        /// <summary>
        /// 创建指定轴的基本旋转矩阵
        /// </summary>
        /// <param name="axis">旋转轴（世界空间向量，将被归一化）</param>
        /// <param name="angle">旋转角度（弧度）</param>
        /// <returns>绕指定轴的旋转矩阵</returns>
        /// <exception cref="ArgumentException">当旋转轴为零向量时抛出</exception>
        private Matrix4x4 CreateAxisRotationMatrix(Vector3 axis, float angle)
        {
            if (axis == Vector3.Zero)
                throw new ArgumentException("旋转轴向量不能为零向量");

            return Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(axis), angle);
        }

        /// <summary>
        /// 按指定顺序组合三个轴的旋转矩阵
        /// 注意：矩阵乘法顺序会影响最终旋转结果
        /// </summary>
        /// <param name="xMatrix">绕X轴的旋转矩阵</param>
        /// <param name="yMatrix">绕Y轴的旋转矩阵</param>
        /// <param name="zMatrix">绕Z轴的旋转矩阵</param>
        /// <param name="order">旋转顺序枚举值</param>
        /// <returns>组合后的旋转矩阵</returns>
        private Matrix4x4 CombineRotationMatrices(Matrix4x4 xMatrix, Matrix4x4 yMatrix, Matrix4x4 zMatrix, RotationOrder order)
        {
            switch (order)
            {
                case RotationOrder.XYZ:
                    return Matrix4x4.Multiply(zMatrix, Matrix4x4.Multiply(yMatrix, xMatrix));
                case RotationOrder.XZY:
                    return Matrix4x4.Multiply(yMatrix, Matrix4x4.Multiply(zMatrix, xMatrix));
                case RotationOrder.YXZ:
                    return Matrix4x4.Multiply(zMatrix, Matrix4x4.Multiply(xMatrix, yMatrix));
                case RotationOrder.YZX:
                    return Matrix4x4.Multiply(xMatrix, Matrix4x4.Multiply(zMatrix, yMatrix));
                case RotationOrder.ZXY:
                    return Matrix4x4.Multiply(yMatrix, Matrix4x4.Multiply(xMatrix, zMatrix));
                case RotationOrder.ZYX:
                default:
                    return Matrix4x4.Multiply(xMatrix, Matrix4x4.Multiply(yMatrix, zMatrix));
            }
        }

        /// <summary>
        /// 创建变换矩阵和逆变换矩阵
        /// 变换矩阵结构：[xAxis; yAxis; zAxis; origin]
        /// </summary>
        /// <returns>包含变换矩阵和逆矩阵的元组</returns>
        /// <exception cref="InvalidOperationException">当无法计算逆矩阵时抛出</exception>
        private (Matrix4x4 transform, Matrix4x4 inverse) CreateTransformationMatrices()
        {
            var matrix = new Matrix4x4(
                _xAxis.X, _yAxis.X, _zAxis.X, 0,
                _xAxis.Y, _yAxis.Y, _zAxis.Y, 0,
                _xAxis.Z, _yAxis.Z, _zAxis.Z, 0,
                _origin.X, _origin.Y, _origin.Z, 1);

            if (!Matrix4x4.Invert(matrix, out var inverse))
                throw new InvalidOperationException("无法计算坐标系的逆变换矩阵");

            return (matrix, inverse);
        }

        /// <summary>
        /// 创建仅包含旋转部分的矩阵（不包含平移）
        /// </summary>
        /// <returns>旋转矩阵</returns>
        private Matrix4x4 CreateRotationMatrix()
        {
            return new Matrix4x4(
                _xAxis.X, _yAxis.X, _zAxis.X, 0,
                _xAxis.Y, _yAxis.Y, _zAxis.Y, 0,
                _xAxis.Z, _yAxis.Z, _zAxis.Z, 0,
                0, 0, 0, 1);
        }

        /// <summary>
        /// 判断三个向量是否共面
        /// 原理：计算混合积的绝对值，小于阈值则认为共面
        /// </summary>
        /// <param name="a">向量a</param>
        /// <param name="b">向量b</param>
        /// <param name="c">向量c</param>
        /// <returns>共面返回true，否则false</returns>
        private bool IsCoplanar(Vector3 a, Vector3 b, Vector3 c)
            => MathF.Abs(Vector3.Dot(a, Vector3.Cross(b, c))) < 0.0001f;

        /// <summary>
        /// 将世界坐标转换为局部坐标
        /// </summary>
        /// <param name="worldPoint">世界空间点</param>
        /// <returns>对应的局部空间点</returns>
        public Vector3 WorldToLocal(Vector3 worldPoint)
            => Vector3.Transform(worldPoint, _inverseTransformationMatrix);

        /// <summary>
        /// 将局部坐标转换为世界坐标
        /// </summary>
        /// <param name="localPoint">局部空间点</param>
        /// <returns>对应的世界空间点</returns>
        public Vector3 LocalToWorld(Vector3 localPoint)
            => Vector3.Transform(localPoint, _transformationMatrix);

        /// <summary>
        /// 将当前坐标系中的局部点转换到目标坐标系的局部表示
        /// </summary>
        /// <param name="targetSystem">目标坐标系</param>
        /// <param name="localPoint">当前坐标系中的局部点</param>
        /// <returns>目标坐标系中的局部点</returns>
        public Vector3 ConvertTo(CoordinateSystem targetSystem, Vector3 localPoint)
        {
            Matrix4x4 combinedMatrix = Matrix4x4.Multiply(
                targetSystem.InverseTransformationMatrix,
                _transformationMatrix);

            return Vector3.Transform(localPoint, combinedMatrix);
        }

        /// <summary>
        /// 将源坐标系中的局部点转换到当前坐标系的局部表示
        /// </summary>
        /// <param name="sourceSystem">源坐标系</param>
        /// <param name="localPoint">源坐标系中的局部点</param>
        /// <returns>当前坐标系中的局部点</returns>
        public Vector3 ConvertFrom(CoordinateSystem sourceSystem, Vector3 localPoint)
        {
            Matrix4x4 combinedMatrix = Matrix4x4.Multiply(
                InverseTransformationMatrix,
                sourceSystem.TransformationMatrix);

            return Vector3.Transform(localPoint, combinedMatrix);
        }

        /// <summary>
        /// 将局部方向向量转换为世界方向向量（仅旋转，不考虑平移）
        /// </summary>
        /// <param name="direction">局部方向向量</param>
        /// <returns>世界方向向量</returns>
        public Vector3 TransformDirection(Vector3 direction)
            => Vector3.TransformNormal(direction, _rotationMatrix);

        /// <summary>
        /// 将世界方向向量转换为局部方向向量（仅旋转，不考虑平移）
        /// </summary>
        /// <param name="direction">世界方向向量</param>
        /// <returns>局部方向向量</returns>
        public Vector3 InverseTransformDirection(Vector3 direction)
        {
            if (Matrix4x4.Invert(_rotationMatrix, out var inv))
                return Vector3.TransformNormal(direction, inv);
            return Vector3.TransformNormal(direction, _rotationMatrix);
        }

        /// <summary>
        /// 平移坐标系生成新实例（保持方向不变）
        /// </summary>
        /// <param name="translation">平移向量（世界空间）</param>
        /// <returns>平移后的新坐标系</returns>
        public CoordinateSystem Translate(Vector3 translation)
            => new CoordinateSystem(_origin + translation, _xAxis, _yAxis, _zAxis);

        /// <summary>
        /// 绕坐标系自身的X轴旋转指定角度，生成新的坐标系
        /// 旋转遵循右手定则（拇指指向X轴正方向，四指为旋转方向）
        /// </summary>
        /// <param name="angle">旋转角度（弧度）</param>
        /// <returns>旋转后的新坐标系</returns>
        public CoordinateSystem RotateX(float angle)
        {
            // 创建绕X轴的旋转矩阵
            Matrix4x4 rotationMatrix = Matrix4x4.CreateFromAxisAngle(_xAxis, angle);

            // 旋转Y和Z轴
            Vector3 newY = Vector3.TransformNormal(_yAxis, rotationMatrix);
            Vector3 newZ = Vector3.TransformNormal(_zAxis, rotationMatrix);

            // 原点保持不变，因为这是纯旋转
            return new CoordinateSystem(_origin, _xAxis, newY, newZ);
        }

        /// <summary>
        /// 绕坐标系自身的Y轴旋转指定角度，生成新的坐标系
        /// 旋转遵循右手定则（拇指指向Y轴正方向，四指为旋转方向）
        /// </summary>
        /// <param name="angle">旋转角度（弧度）</param>
        /// <returns>旋转后的新坐标系</returns>
        public CoordinateSystem RotateY(float angle)
        {
            // 创建绕Y轴的旋转矩阵
            Matrix4x4 rotationMatrix = Matrix4x4.CreateFromAxisAngle(_yAxis, angle);

            // 旋转X和Z轴
            Vector3 newX = Vector3.TransformNormal(_xAxis, rotationMatrix);
            Vector3 newZ = Vector3.TransformNormal(_zAxis, rotationMatrix);

            return new CoordinateSystem(_origin, newX, _yAxis, newZ);
        }

        /// <summary>
        /// 绕坐标系自身的Z轴旋转指定角度，生成新的坐标系
        /// 旋转遵循右手定则（拇指指向Z轴正方向，四指为旋转方向）
        /// </summary>
        /// <param name="angle">旋转角度（弧度）</param>
        /// <returns>旋转后的新坐标系</returns>
        public CoordinateSystem RotateZ(float angle)
        {
            // 创建绕Z轴的旋转矩阵
            Matrix4x4 rotationMatrix = Matrix4x4.CreateFromAxisAngle(_zAxis, angle);

            // 旋转X和Y轴
            Vector3 newX = Vector3.TransformNormal(_xAxis, rotationMatrix);
            Vector3 newY = Vector3.TransformNormal(_yAxis, rotationMatrix);

            return new CoordinateSystem(_origin, newX, newY, _zAxis);
        }

        /// <summary>
        /// 绕任意轴旋转指定角度，生成新的坐标系
        /// 旋转轴为局部坐标系中的向量，会先转换到世界空间
        /// </summary>
        /// <param name="axis">局部坐标系中的旋转轴向量（非零）</param>
        /// <param name="angle">旋转角度（弧度）</param>
        /// <returns>旋转后的新坐标系</returns>
        /// <exception cref="ArgumentException">当旋转轴为零向量时抛出</exception>
        public CoordinateSystem RotateAroundAxis(Vector3 axis, float angle)
        {
            if (axis == Vector3.Zero)
                throw new ArgumentException("旋转轴向量不能为零向量");

            // 将局部轴转换为世界空间
            Vector3 worldAxis = LocalToWorldDirection(axis);
            worldAxis = Vector3.Normalize(worldAxis);

            // 创建绕世界轴的旋转矩阵
            Matrix4x4 rotationMatrix = Matrix4x4.CreateFromAxisAngle(worldAxis, angle);

            // 旋转所有轴向量
            Vector3 newX = Vector3.TransformNormal(_xAxis, rotationMatrix);
            Vector3 newY = Vector3.TransformNormal(_yAxis, rotationMatrix);
            Vector3 newZ = Vector3.TransformNormal(_zAxis, rotationMatrix);

            return new CoordinateSystem(_origin, newX, newY, newZ);
        }

        /// <summary>
        /// 将局部方向向量转换为世界方向向量（仅旋转部分）
        /// </summary>
        /// <param name="localDirection">局部方向向量</param>
        /// <returns>世界方向向量</returns>
        public Vector3 LocalToWorldDirection(Vector3 localDirection)
        {
            return Vector3.TransformNormal(localDirection, _rotationMatrix);
        }

        /// <summary>
        /// 将世界方向向量转换为局部方向向量（仅旋转部分）
        /// </summary>
        /// <param name="worldDirection">世界方向向量</param>
        /// <returns>局部方向向量</returns>
        public Vector3 WorldToLocalDirection(Vector3 worldDirection)
        {
            if (Matrix4x4.Invert(_rotationMatrix, out var invRotation))
                return Vector3.TransformNormal(worldDirection, invRotation);
            return Vector3.TransformNormal(worldDirection, _rotationMatrix);
        }

        /// <summary>
        /// 按指定顺序和角度绕三个轴旋转，生成新的坐标系
        /// </summary>
        /// <param name="xAngle">绕X轴旋转角度（弧度）</param>
        /// <param name="yAngle">绕Y轴旋转角度（弧度）</param>
        /// <param name="zAngle">绕Z轴旋转角度（弧度）</param>
        /// <param name="order">旋转顺序（默认ZYX顺序）</param>
        /// <returns>旋转后的新坐标系</returns>
        public CoordinateSystem Rotate(float xAngle, float yAngle, float zAngle, RotationOrder order = RotationOrder.ZYX)
        {
            CoordinateSystem result = this;

            switch (order)
            {
                case RotationOrder.XYZ:
                    result = result.RotateX(xAngle).RotateY(yAngle).RotateZ(zAngle);
                    break;
                case RotationOrder.XZY:
                    result = result.RotateX(xAngle).RotateZ(zAngle).RotateY(yAngle);
                    break;
                case RotationOrder.YXZ:
                    result = result.RotateY(yAngle).RotateX(xAngle).RotateZ(zAngle);
                    break;
                case RotationOrder.YZX:
                    result = result.RotateY(yAngle).RotateZ(zAngle).RotateX(xAngle);
                    break;
                case RotationOrder.ZXY:
                    result = result.RotateZ(zAngle).RotateX(xAngle).RotateY(yAngle);
                    break;
                case RotationOrder.ZYX:
                default:
                    result = result.RotateZ(zAngle).RotateY(yAngle).RotateX(xAngle);
                    break;
            }

            return result;
        }

        /// <summary>
        /// 判断坐标系是否为正交坐标系（三轴互相垂直）
        /// </summary>
        /// <returns>正交返回true，否则false</returns>
        public bool IsOrthogonal()
            => MathF.Abs(Vector3.Dot(_xAxis, _yAxis)) < 0.0001f &&
               MathF.Abs(Vector3.Dot(_xAxis, _zAxis)) < 0.0001f &&
               MathF.Abs(Vector3.Dot(_yAxis, _zAxis)) < 0.0001f;

        /// <summary>
        /// 判断坐标系是否为右手坐标系
        /// 右手定则：X×Y应当指向Z轴正方向
        /// </summary>
        /// <returns>右手系返回true，否则false</returns>
        public bool IsRightHanded()
            => Vector3.Dot(_xAxis, Vector3.Cross(_yAxis, _zAxis)) > 0;

        /// <summary>
        /// 提取坐标系的欧拉角（ZYX顺序，弧度制）
        /// 处理了万向节锁（Gimbal Lock）情况
        /// </summary>
        /// <returns>包含X、Y、Z轴旋转角度的向量</returns>
        public Vector3 GetEulerAngles()
        {
            float m11 = _rotationMatrix.M11, m12 = _rotationMatrix.M12, m13 = _rotationMatrix.M13;
            float m21 = _rotationMatrix.M21, m22 = _rotationMatrix.M22, m23 = _rotationMatrix.M23;
            float m31 = _rotationMatrix.M31, m32 = _rotationMatrix.M32, m33 = _rotationMatrix.M33;

            // 处理万向节锁（当pitch接近±90度时，yaw和roll会产生耦合）
            bool singular = MathF.Abs(m23) > 0.9999f;
            float x, y, z;

            if (!singular)
            {
                x = MathF.Atan2(m32, m33);          // Roll (X轴旋转)
                y = MathF.Atan2(-m31, m23);         // Pitch (Y轴旋转)
                z = MathF.Atan2(m21, m11);          // Yaw (Z轴旋转)
            }
            else
            {
                x = MathF.Atan2(-m21, m22);         // Roll (X轴旋转)
                y = MathF.Atan2(-m31, m23);         // Pitch (Y轴旋转)
                z = 0;                              // Yaw (Z轴旋转固定为0)
            }

            return new Vector3(x, y, z);
        }

        /// <summary>
        /// 创建相机坐标系（遵循OpenGL相机模型）
        /// 注意：z轴指向相机观察方向的反方向
        /// </summary>
        /// <param name="position">相机位置</param>
        /// <param name="lookAt">相机看向的点</param>
        /// <param name="up">世界空间的上方向</param>
        /// <returns>相机坐标系</returns>
        public static CoordinateSystem CreateCamera(Vector3 position, Vector3 lookAt, Vector3 up)
        {
            Vector3 zAxis = Vector3.Normalize(position - lookAt);
            Vector3 xAxis = Vector3.Normalize(Vector3.Cross(Vector3.Normalize(up), zAxis));
            Vector3 yAxis = Vector3.Cross(zAxis, xAxis);

            return new CoordinateSystem(position, xAxis, yAxis, zAxis);
        }

        /// <summary>
        /// 返回坐标系的字符串表示（默认格式）
        /// </summary>
        /// <returns>包含原点和三轴向量的字符串</returns>
        public override string ToString()
            => $"Origin: {_origin}, X: {_xAxis}, Y: {_yAxis}, Z: {_zAxis}";

        /// <summary>
        /// 返回坐标系的字符串表示（指定格式）
        /// </summary>
        /// <param name="format">数值格式字符串</param>
        /// <returns>格式化后的字符串</returns>
        public string ToString(string format)
            => $"Origin: {_origin.ToString(format)}, X: {_xAxis.ToString(format)}, Y: {_yAxis.ToString(format)}, Z: {_zAxis.ToString(format)}";

        /// <summary>
        /// 获取当前坐标系相对于父坐标系的局部表示
        /// 结果坐标系的原点和轴向量均表示为父坐标系中的局部坐标
        /// </summary>
        /// <param name="parent">父坐标系</param>
        /// <returns>相对于父坐标系的新坐标系</returns>
        public CoordinateSystem GetRelativeTo(CoordinateSystem parent)
        {
            Vector3 relativeOrigin = parent.WorldToLocal(_origin);
            Vector3 relativeX = parent.InverseTransformDirection(_xAxis);
            Vector3 relativeY = parent.InverseTransformDirection(_yAxis);
            Vector3 relativeZ = parent.InverseTransformDirection(_zAxis);

            return new CoordinateSystem(relativeOrigin, relativeX, relativeY, relativeZ);
        }

        /// <summary>
        /// 判断两个坐标系是否近似相等（考虑容差）
        /// 原点使用较大容差，方向使用较小容差（角度判断）
        /// </summary>
        /// <param name="other">另一个坐标系</param>
        /// <param name="tolerance">距离容差（默认0.001）</param>
        /// <returns>近似相等返回true，否则false</returns>
        public bool Approximately(CoordinateSystem other, float tolerance = 0.001f)
        {
            // 原点距离容差放大（原点通常变化范围更大）
            if (Vector3.Distance(_origin, other._origin) > tolerance * 10)
                return false;

            // 坐标轴夹角余弦值判断（更准确的方向近似）
            float dotX = Vector3.Dot(_xAxis, other._xAxis);
            float dotY = Vector3.Dot(_yAxis, other._yAxis);
            float dotZ = Vector3.Dot(_zAxis, other._zAxis);

            // 余弦值大于 0.9999 表示夹角小于 1度（使用更严格的容差）
            return dotX > 0.9999f && dotY > 0.9999f && dotZ > 0.9999f;
        }

        /// <summary>
        /// 创建坐标系的深拷贝
        /// </summary>
        /// <returns>新的坐标系实例</returns>
        public CoordinateSystem Clone()
        {
            return new CoordinateSystem(_origin, _xAxis, _yAxis, _zAxis);
        }

        /// <summary>
        /// 计算方向向量与X轴的夹角（角度制）
        /// </summary>
        /// <param name="direction">任意方向向量</param>
        /// <returns>夹角（0-180度）</returns>
        public float AngleToXAxis(Vector3 direction)
            => MathF.Acos(Vector3.Dot(Vector3.Normalize(direction), _xAxis)) * (180f / MathF.PI);

        /// <summary>
        /// 计算方向向量与Y轴的夹角（角度制）
        /// </summary>
        /// <param name="direction">任意方向向量</param>
        /// <returns>夹角（0-180度）</returns>
        public float AngleToYAxis(Vector3 direction)
            => MathF.Acos(Vector3.Dot(Vector3.Normalize(direction), _yAxis)) * (180f / MathF.PI);

        /// <summary>
        /// 计算方向向量与Z轴的夹角（角度制）
        /// </summary>
        /// <param name="direction">任意方向向量</param>
        /// <returns>夹角（0-180度）</returns>
        public float AngleToZAxis(Vector3 direction)
            => MathF.Acos(Vector3.Dot(Vector3.Normalize(direction), _zAxis)) * (180f / MathF.PI);
    }

    /// <summary>
    /// 定义坐标系旋转顺序枚举（支持6种标准欧拉角旋转顺序）
    /// 注意：不同顺序会产生不同的旋转结果
    /// </summary>
    public enum RotationOrder
    {
        /// <summary>X→Y→Z旋转顺序</summary>
        XYZ,
        /// <summary>X→Z→Y旋转顺序</summary>
        XZY,
        /// <summary>Y→X→Z旋转顺序</summary>
        YXZ,
        /// <summary>Y→Z→X旋转顺序</summary>
        YZX,
        /// <summary>Z→X→Y旋转顺序</summary>
        ZXY,
        /// <summary>Z→Y→X旋转顺序（默认顺序）</summary>
        ZYX
    }
}