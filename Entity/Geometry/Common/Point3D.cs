using System;
using System.Numerics;

namespace Entity.Geometry.Common
{
    /// <summary>
    /// 表示三维空间中的坐标点，支持在不同坐标系间转换
    /// 该类采用不可变设计，所有操作均返回新的点实例
    /// </summary>
    public sealed class Point3D : IEquatable<Point3D>
    {
        /// <summary>点所属的坐标系</summary>
        private readonly CoordinateSystem _coordinateSystem;

        /// <summary>点的坐标值（在所属坐标系中的局部表示）</summary>
        private Vector3 _localCoordinates;

        /// <summary>点在世界坐标系中的坐标值（缓存值）</summary>
        private Vector3 _worldCoordinates;

        /// <summary>点的坐标值（局部坐标系），只读</summary>
        public Vector3 LocalCoordinates => _localCoordinates;

        /// <summary>点所属的坐标系，只读</summary>
        public CoordinateSystem CoordinateSystem => _coordinateSystem;

        /// <summary>点在世界坐标系中的坐标值，只读</summary>
        public Vector3 WorldCoordinates => _worldCoordinates;

        /// <summary>
        /// 使用指定的局部坐标和坐标系初始化点
        /// </summary>
        /// <param name="coordinates">局部坐标系中的坐标值</param>
        /// <param name="coordinateSystem">所属的坐标系</param>
        public Point3D(Vector3 coordinates, CoordinateSystem coordinateSystem)
        {
            _coordinateSystem = coordinateSystem ?? throw new ArgumentNullException(nameof(coordinateSystem));
            _localCoordinates = coordinates;
            _worldCoordinates = _coordinateSystem.LocalToWorld(coordinates);
        }

        /// <summary>
        /// 使用指定的世界坐标和坐标系初始化点
        /// </summary>
        /// <param name="worldCoordinates">世界坐标系中的坐标值</param>
        /// <param name="coordinateSystem">所属的坐标系</param>
        public Point3D(Vector3 worldCoordinates, CoordinateSystem coordinateSystem, bool isWorldCoordinates = true)
        {
            _coordinateSystem = coordinateSystem ?? throw new ArgumentNullException(nameof(coordinateSystem));
            _worldCoordinates = worldCoordinates;
            _localCoordinates = _coordinateSystem.WorldToLocal(worldCoordinates);
        }

        /// <summary>
        /// 将点转换到指定的目标坐标系
        /// </summary>
        /// <param name="targetSystem">目标坐标系</param>
        /// <returns>目标坐标系中的新点</returns>
        public Point3D ConvertTo(CoordinateSystem targetSystem)
        {
            if (ReferenceEquals(_coordinateSystem, targetSystem))
                return this;

            // 使用坐标系转换方法计算新坐标
            Vector3 newLocalCoordinates = _coordinateSystem.ConvertTo(targetSystem, LocalCoordinates);
            return new Point3D(newLocalCoordinates, targetSystem);
        }

        /// <summary>
        /// 将点从源坐标系转换到当前坐标系
        /// </summary>
        /// <param name="sourcePoint">源坐标系中的点</param>
        /// <returns>当前坐标系中的新点</returns>
        public Point3D ConvertFrom(Point3D sourcePoint)
        {
            if (ReferenceEquals(_coordinateSystem, sourcePoint._coordinateSystem))
                return new Point3D(sourcePoint.LocalCoordinates, _coordinateSystem);

            // 使用坐标系转换方法计算新坐标
            Vector3 newLocalCoordinates = _coordinateSystem.ConvertFrom(
                sourcePoint._coordinateSystem, sourcePoint.LocalCoordinates);
            return new Point3D(newLocalCoordinates, _coordinateSystem);
        }

        /// <summary>
        /// 平移点生成新实例（在当前坐标系中）
        /// </summary>
        /// <param name="translation">平移向量（局部坐标系）</param>
        /// <returns>平移后的新点</returns>
        public Point3D TranslateL(Vector3 translation)
            => new Point3D(_localCoordinates + translation, _coordinateSystem);

        /// <summary>
        /// 在当前坐标系中对点进行缩放
        /// </summary>
        /// <param name="scaleFactor">缩放因子</param>
        /// <returns>缩放后的新点</returns>
        public Point3D ScaleL(float scaleFactor)
            => new Point3D(_localCoordinates * scaleFactor, _coordinateSystem);

        /// <summary>
        /// 在当前坐标系中对点进行缩放
        /// </summary>
        /// <param name="scaleVector">各轴的缩放因子</param>
        /// <returns>缩放后的新点</returns>
        public Point3D ScaleL(Vector3 scaleVector)
            => new Point3D(new Vector3(
                _localCoordinates.X * scaleVector.X,
                _localCoordinates.Y * scaleVector.Y,
                _localCoordinates.Z * scaleVector.Z),
                _coordinateSystem);

        /// <summary>
        /// 平移点生成新实例（在世界坐标系中）
        /// </summary>
        /// <param name="translation">平移向量（局部坐标系）</param>
        /// <returns>平移后的新点</returns>
        public Point3D TranslateW(Vector3 translation)
            => new Point3D(_worldCoordinates + translation, _coordinateSystem);

        /// <summary>
        /// 在世界坐标系中对点进行缩放
        /// </summary>
        /// <param name="scaleFactor">缩放因子</param>
        /// <returns>缩放后的新点</returns>
        public Point3D ScaleW(float scaleFactor)
            => new Point3D(_worldCoordinates * scaleFactor, _coordinateSystem);

        /// <summary>
        /// 在世界坐标系中对点进行缩放
        /// </summary>
        /// <param name="scaleVector">各轴的缩放因子</param>
        /// <returns>缩放后的新点</returns>
        public Point3D ScaleW(Vector3 scaleVector)
            => new Point3D(new Vector3(
                _worldCoordinates.X * scaleVector.X,
                _worldCoordinates.Y * scaleVector.Y,
                _worldCoordinates.Z * scaleVector.Z),
                _coordinateSystem);

        /// <summary>
        /// 计算两点之间的距离（世界坐标系）
        /// </summary>
        /// <param name="other">另一个点</param>
        /// <returns>两点间的距离</returns>
        public float DistanceTo(Point3D other)
        {
            Vector3 worldThis = WorldCoordinates;
            Vector3 worldOther = other.WorldCoordinates;
            return Vector3.Distance(worldThis, worldOther);
        }

        /// <summary>
        /// 计算两点之间的平方距离（世界坐标系，避免开平方运算）
        /// </summary>
        /// <param name="other">另一个点</param>
        /// <returns>两点间距离的平方</returns>
        public float DistanceToSquared(Point3D other)
        {
            Vector3 worldThis = WorldCoordinates;
            Vector3 worldOther = other.WorldCoordinates;
            return Vector3.DistanceSquared(worldThis, worldOther);
        }

        /// <summary>
        /// 判断两个点是否相等（考虑容差）
        /// </summary>
        /// <param name="point1">第一个点</param>
        /// <param name="point2">第二个点</param>
        /// <param name="tolerance">距离容差（默认0.001）</param>
        /// <returns>相等返回true，否则false</returns>
        public static bool Approximately(Point3D point1, Point3D point2, float tolerance = 0.001f)
        {
            if (point1 is null || point2 is null)
                return false;

            if (!ReferenceEquals(point1._coordinateSystem, point2._coordinateSystem))
            {
                // 如果坐标系不同，先转换到同一坐标系再比较
                point2 = point2.ConvertTo(point1._coordinateSystem);
            }

            return Vector3.DistanceSquared(point1.LocalCoordinates, point2.LocalCoordinates) < tolerance * tolerance;
        }

        /// <summary>
        /// 判断当前点是否等于另一个点
        /// </summary>
        /// <param name="other">要比较的点</param>
        /// <returns>相等返回true，否则false</returns>
        public bool Equals(Point3D other)
        {
            if (other is null)
                return false;

            if (ReferenceEquals(this, other))
                return true;

            if (!ReferenceEquals(_coordinateSystem, other._coordinateSystem))
                return false;

            return LocalCoordinates == other.LocalCoordinates;
        }

        /// <summary>
        /// 判断当前点是否等于另一个对象
        /// </summary>
        /// <param name="obj">要比较的对象</param>
        /// <returns>相等返回true，否则false</returns>
        public override bool Equals(object obj)
            => obj is Point3D other && Equals(other);

        /// <summary>
        /// 获取点的哈希码
        /// </summary>
        /// <returns>哈希码</returns>
        public override int GetHashCode()
            => HashCode.Combine(LocalCoordinates, _coordinateSystem);

        /// <summary>
        /// 返回点的字符串表示（默认格式）
        /// </summary>
        /// <returns>包含坐标和坐标系信息的字符串</returns>
        public string ToStringL()
            => $"{{{LocalCoordinates.X:F3}, {LocalCoordinates.Y:F3}, {LocalCoordinates.Z:F3}}} in {_coordinateSystem.ToString()}";

        /// <summary>
        /// 返回点的字符串表示（指定格式）
        /// </summary>
        /// <param name="format">数值格式字符串</param>
        /// <returns>格式化后的字符串</returns>
        public string ToStringL(string format)
            => $"{{{LocalCoordinates.X.ToString(format)}, {LocalCoordinates.Y.ToString(format)}, {LocalCoordinates.Z.ToString(format)}}} in {_coordinateSystem.ToString(format)}";

        /// <summary>
        /// 返回点的字符串表示（默认格式）
        /// </summary>
        /// <returns>包含坐标和坐标系信息的字符串</returns>
        public string ToStringW()
            => $"{{{WorldCoordinates.X:F3}, {WorldCoordinates.Y:F3}, {WorldCoordinates.Z:F3}}}";

        /// <summary>
        /// 返回点的字符串表示（指定格式）
        /// </summary>
        /// <param name="format">数值格式字符串</param>
        /// <returns>格式化后的字符串</returns>
        public string ToStringW(string format)
            => $"{{{WorldCoordinates.X.ToString(format)}, {WorldCoordinates.Y.ToString(format)}, {WorldCoordinates.Z.ToString(format)}}}";


        /// <summary>
        /// 创建点的深拷贝
        /// </summary>
        /// <returns>新的点实例</returns>
        public Point3D Clone()
            => new Point3D(LocalCoordinates, _coordinateSystem);
    }
}