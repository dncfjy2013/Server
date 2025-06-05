using System;
using System.Numerics;

namespace Entity.Geometry
{
    /// <summary>
    /// 表示三维空间中的自定义坐标系，支持坐标转换、旋转、平移等几何操作
    /// </summary>
    public class CoordinateSystem
    {
        private readonly Vector3 _origin;
        private readonly Vector3 _xAxis;
        private readonly Vector3 _yAxis;
        private readonly Vector3 _zAxis;
        private readonly Matrix4x4 _transformationMatrix;
        private readonly Matrix4x4 _inverseTransformationMatrix;
        private readonly Matrix4x4 _rotationMatrix;

        public Vector3 Origin => _origin;
        public Vector3 XAxis => _xAxis;
        public Vector3 YAxis => _yAxis;
        public Vector3 ZAxis => _zAxis;
        public Matrix4x4 TransformationMatrix => _transformationMatrix;
        public Matrix4x4 InverseTransformationMatrix => _inverseTransformationMatrix;
        public Matrix4x4 RotationMatrix => _rotationMatrix;

        public CoordinateSystem()
            : this(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ)
        {
        }

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
        // 按顺序对三轴进行旋转的构造函数
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

        // 辅助方法：创建指定轴的旋转矩阵
        private Matrix4x4 CreateAxisRotationMatrix(Vector3 axis, float angle)
        {
            if (axis == Vector3.Zero)
                throw new ArgumentException("旋转轴向量不能为零向量");

            return Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(axis), angle);
        }

        // 辅助方法：按指定顺序组合旋转矩阵
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

        private Matrix4x4 CreateRotationMatrix()
        {
            return new Matrix4x4(
                _xAxis.X, _yAxis.X, _zAxis.X, 0,
                _xAxis.Y, _yAxis.Y, _zAxis.Y, 0,
                _xAxis.Z, _yAxis.Z, _zAxis.Z, 0,
                0, 0, 0, 1);
        }

        private bool IsCoplanar(Vector3 a, Vector3 b, Vector3 c)
            => MathF.Abs(Vector3.Dot(a, Vector3.Cross(b, c))) < 0.0001f;

        public Vector3 WorldToLocal(Vector3 worldPoint)
            => Vector3.Transform(worldPoint, _inverseTransformationMatrix);

        public Vector3 LocalToWorld(Vector3 localPoint)
            => Vector3.Transform(localPoint, _transformationMatrix);

        public Vector3 ConvertTo(CoordinateSystem targetSystem, Vector3 localPoint)
        {
            Matrix4x4 combinedMatrix = Matrix4x4.Multiply(
                targetSystem.InverseTransformationMatrix,
                _transformationMatrix);

            return Vector3.Transform(localPoint, combinedMatrix);
        }

        public Vector3 ConvertFrom(CoordinateSystem sourceSystem, Vector3 localPoint)
        {
            Matrix4x4 combinedMatrix = Matrix4x4.Multiply(
                InverseTransformationMatrix,
                sourceSystem.TransformationMatrix);

            return Vector3.Transform(localPoint, combinedMatrix);
        }

        // 方向向量转换方法修正
        public Vector3 TransformDirection(Vector3 direction)
            => Vector3.TransformNormal(direction, _rotationMatrix);

        public Vector3 InverseTransformDirection(Vector3 direction)
        {
            if (Matrix4x4.Invert(_rotationMatrix, out var inv))
                return Vector3.TransformNormal(direction, inv);
            return Vector3.TransformNormal(direction, _rotationMatrix);
        }

        public CoordinateSystem Translate(Vector3 translation)
            => new CoordinateSystem(_origin + translation, _xAxis, _yAxis, _zAxis);

        public bool IsOrthogonal()
            => MathF.Abs(Vector3.Dot(_xAxis, _yAxis)) < 0.0001f &&
               MathF.Abs(Vector3.Dot(_xAxis, _zAxis)) < 0.0001f &&
               MathF.Abs(Vector3.Dot(_yAxis, _zAxis)) < 0.0001f;

        public bool IsRightHanded()
            => Vector3.Dot(_xAxis, Vector3.Cross(_yAxis, _zAxis)) > 0;

        // 欧拉角提取方法修正
        public Vector3 GetEulerAngles()
        {
            float m11 = _rotationMatrix.M11, m12 = _rotationMatrix.M12, m13 = _rotationMatrix.M13;
            float m21 = _rotationMatrix.M21, m22 = _rotationMatrix.M22, m23 = _rotationMatrix.M23;
            float m31 = _rotationMatrix.M31, m32 = _rotationMatrix.M32, m33 = _rotationMatrix.M33;

            // 处理万向节锁（Gimbal Lock）
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

        public static CoordinateSystem CreateCamera(Vector3 position, Vector3 lookAt, Vector3 up)
        {
            Vector3 zAxis = Vector3.Normalize(position - lookAt);
            Vector3 xAxis = Vector3.Normalize(Vector3.Cross(Vector3.Normalize(up), zAxis));
            Vector3 yAxis = Vector3.Cross(zAxis, xAxis);

            return new CoordinateSystem(position, xAxis, yAxis, zAxis);
        }

        public override string ToString()
            => $"Origin: {_origin}, X: {_xAxis}, Y: {_yAxis}, Z: {_zAxis}";

        public string ToString(string format)
            => $"Origin: {_origin.ToString(format)}, X: {_xAxis.ToString(format)}, Y: {_yAxis.ToString(format)}, Z: {_zAxis.ToString(format)}";

        public CoordinateSystem GetRelativeTo(CoordinateSystem parent)
        {
            Vector3 relativeOrigin = parent.WorldToLocal(_origin);
            Vector3 relativeX = parent.InverseTransformDirection(_xAxis);
            Vector3 relativeY = parent.InverseTransformDirection(_yAxis);
            Vector3 relativeZ = parent.InverseTransformDirection(_zAxis);

            return new CoordinateSystem(relativeOrigin, relativeX, relativeY, relativeZ);
        }

        // 近似判断方法优化
        public bool Approximately(CoordinateSystem other, float tolerance = 0.001f)
        {
            // 原点距离容差放大（原点通常变化范围更大）
            if (Vector3.Distance(_origin, other._origin) > tolerance * 10)
                return false;

            // 坐标轴夹角余弦值判断（更准确的方向近似）
            float dotX = Vector3.Dot(_xAxis, other._xAxis);
            float dotY = Vector3.Dot(_yAxis, other._yAxis);
            float dotZ = Vector3.Dot(_zAxis, other._zAxis);

            // 余弦值大于 0.999 表示夹角小于 1度，使用更严格的容差
            return dotX > 0.9999f && dotY > 0.9999f && dotZ > 0.9999f;
        }

        public CoordinateSystem Clone()
        {
            return new CoordinateSystem(_origin, _xAxis, _yAxis, _zAxis);
        }

        public float AngleToXAxis(Vector3 direction)
            => MathF.Acos(Vector3.Dot(Vector3.Normalize(direction), _xAxis)) * (180f / MathF.PI);

        public float AngleToYAxis(Vector3 direction)
            => MathF.Acos(Vector3.Dot(Vector3.Normalize(direction), _yAxis)) * (180f / MathF.PI);

        public float AngleToZAxis(Vector3 direction)
            => MathF.Acos(Vector3.Dot(Vector3.Normalize(direction), _zAxis)) * (180f / MathF.PI);
    }

    /// <summary>
    /// 定义坐标系旋转顺序枚举（支持6种标准欧拉角旋转顺序）
    /// </summary>
    public enum RotationOrder
    {
        XYZ, XZY, YXZ, YZX, ZXY, ZYX
    }
}