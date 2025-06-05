using System;
using System.Numerics;
using Entity.Geometry.Common;

namespace Entity.Geometry
{
    /// <summary>
    /// 表示三维空间中的圆形，支持在指定平面上定义和操作
    /// 采用不可变设计，所有操作返回新的圆形实例
    /// </summary>
    public sealed class Circle
    {
        /// <summary>圆形所在的平面</summary>
        private readonly Plane _plane;

        /// <summary>圆心（局部坐标）</summary>
        private readonly Vector3 _center;

        /// <summary>圆的半径</summary>
        private readonly float _radius;

        /// <summary>圆形所在的平面，只读</summary>
        public Plane Plane => _plane;

        /// <summary>圆心（局部坐标），只读</summary>
        public Vector3 Center => _center;

        /// <summary>圆的半径，只读</summary>
        public float Radius => _radius;

        /// <summary>圆的周长</summary>
        public float Circumference => 2 * MathF.PI * _radius;

        /// <summary>圆的面积</summary>
        public float Area => MathF.PI * _radius * _radius;

        /// <summary>
        /// 在指定平面上初始化圆形
        /// </summary>
        /// <param name="plane">圆形所在的平面</param>
        /// <param name="center">圆心（局部坐标）</param>
        /// <param name="radius">圆的半径</param>
        /// <exception cref="ArgumentException">当半径为负数时抛出</exception>
        public Circle(Plane plane, Vector3 center, float radius)
        {
            if (radius < 0)
                throw new ArgumentException("圆的半径不能为负数", nameof(radius));

            _plane = plane;
            _center = center;
            _radius = radius;
        }

        /// <summary>
        /// 使用圆心、半径和法线方向在指定坐标系下初始化圆形
        /// </summary>
        /// <param name="coordinateSystem">坐标系</param>
        /// <param name="center">圆心（局部坐标）</param>
        /// <param name="radius">圆的半径</param>
        /// <param name="normal">法线方向（局部坐标，将被归一化）</param>
        /// <exception cref="ArgumentException">当半径为负数或法线为零向量时抛出</exception>
        public Circle(CoordinateSystem coordinateSystem, Vector3 center, float radius, Vector3 normal)
            : this(new Plane(coordinateSystem, center, normal), center, radius)
        {
        }

        /// <summary>
        /// 检查点是否在圆上（考虑容差）
        /// </summary>
        /// <param name="point">待检查的点（局部坐标）</param>
        /// <param name="tolerance">容差值（默认0.0001）</param>
        /// <returns>点在圆上返回true，否则false</returns>
        public bool ContainsPoint(Vector3 point, float tolerance = 0.0001f)
        {
            // 首先检查点是否在平面上
            if (!_plane.ContainsPoint(point, tolerance))
                return false;

            // 计算点到圆心的距离
            Vector3 centerToPoint = point - _center;
            float distance = centerToPoint.Length();

            // 检查距离是否近似等于半径
            return MathF.Abs(distance - _radius) < tolerance;
        }

        /// <summary>
        /// 检查点是否在圆内部（包括边界）
        /// </summary>
        /// <param name="point">待检查的点（局部坐标）</param>
        /// <param name="tolerance">容差值（默认0.0001）</param>
        /// <returns>点在圆内部返回true，否则false</returns>
        public bool ContainsPointInside(Vector3 point, float tolerance = 0.0001f)
        {
            // 首先检查点是否在平面上
            if (!_plane.ContainsPoint(point, tolerance))
                return false;

            // 计算点到圆心的距离
            Vector3 centerToPoint = point - _center;
            float distanceSquared = centerToPoint.LengthSquared();

            // 检查距离平方是否小于等于半径平方（考虑容差）
            return distanceSquared <= (_radius * _radius) + tolerance;
        }

        /// <summary>
        /// 计算点到圆的最短距离
        /// </summary>
        /// <param name="point">待计算的点（局部坐标）</param>
        /// <returns>最短距离</returns>
        public float DistanceToPoint(Vector3 point)
        {
            // 计算点在平面上的投影
            Vector3 projectedPoint = _plane.ProjectPoint(point);

            // 计算投影点到圆心的距离
            Vector3 centerToProjected = projectedPoint - _center;
            float distanceToCenter = centerToProjected.Length();

            // 如果投影点在圆内，最短距离是点到平面的距离
            if (distanceToCenter <= _radius)
                return _plane.DistanceToPointUnsigned(point);

            // 否则，最短距离是投影点到圆周的距离加上点到平面的距离
            float distanceToCircumference = distanceToCenter - _radius;
            float distanceToPlane = _plane.DistanceToPointUnsigned(point);
            return MathF.Sqrt(distanceToCircumference * distanceToCircumference + distanceToPlane * distanceToPlane);
        }

        /// <summary>
        /// 将点投影到圆上
        /// 如果点在圆内部，返回点本身；否则返回点在圆周上的投影
        /// </summary>
        /// <param name="point">待投影的点（局部坐标）</param>
        /// <returns>投影后的点（局部坐标）</returns>
        public Vector3 ProjectPoint(Vector3 point)
        {
            // 计算点在平面上的投影
            Vector3 projectedPoint = _plane.ProjectPoint(point);

            // 计算投影点到圆心的向量
            Vector3 centerToProjected = projectedPoint - _center;
            float distanceToCenter = centerToProjected.Length();

            // 如果投影点在圆内或圆上，直接返回
            if (distanceToCenter <= _radius + 0.0001f)
                return projectedPoint;

            // 否则，将向量归一化并乘以半径
            return _center + Vector3.Normalize(centerToProjected) * _radius;
        }

        /// <summary>
        /// 判断直线与圆是否相交
        /// </summary>
        /// <param name="lineStart">直线起点（局部坐标）</param>
        /// <param name="lineDirection">直线方向向量（局部坐标）</param>
        /// <param name="intersectionPoints">交点数组（最多两个）</param>
        /// <param name="tolerance">容差值（默认0.0001）</param>
        /// <returns>相交返回true，否则false</returns>
        public bool IntersectsLine(Vector3 lineStart, Vector3 lineDirection, out Vector3[] intersectionPoints, float tolerance = 0.0001f)
        {
            intersectionPoints = Array.Empty<Vector3>();

            // 计算直线与平面的交点
            if (!_plane.IntersectsLine(lineStart, lineDirection, out Vector3 intersectionPoint, tolerance))
                return false;

            // 检查交点是否在圆上
            if (ContainsPoint(intersectionPoint, tolerance))
            {
                intersectionPoints = new[] { intersectionPoint };
                return true;
            }

            return false;
        }

        /// <summary>
        /// 判断线段与圆是否相交
        /// </summary>
        /// <param name="lineStart">线段起点（局部坐标）</param>
        /// <param name="lineEnd">线段终点（局部坐标）</param>
        /// <param name="intersectionPoints">交点数组（最多两个）</param>
        /// <param name="tolerance">容差值（默认0.0001）</param>
        /// <returns>相交返回true，否则false</returns>
        public bool IntersectsLineSegment(Vector3 lineStart, Vector3 lineEnd, out Vector3[] intersectionPoints, float tolerance = 0.0001f)
        {
            intersectionPoints = Array.Empty<Vector3>();

            // 计算线段与平面的交点
            if (!_plane.IntersectsLineSegment(lineStart, lineEnd, out Vector3 intersectionPoint, tolerance))
                return false;

            // 检查交点是否在圆上
            if (ContainsPoint(intersectionPoint, tolerance))
            {
                intersectionPoints = new[] { intersectionPoint };
                return true;
            }

            return false;
        }

        /// <summary>
        /// 判断两个圆是否相交
        /// 注意：此方法假设两个圆在同一平面上
        /// </summary>
        /// <param name="other">另一个圆</param>
        /// <param name="tolerance">容差值（默认0.0001）</param>
        /// <returns>相交返回true，否则false</returns>
        public bool IntersectsCircle(Circle other, float tolerance = 0.0001f)
        {
            // 检查两个圆是否在同一平面上
            if (!_plane.IsCoplanarWith(other._plane, tolerance))
                throw new InvalidOperationException("两个圆必须在同一平面上才能判断相交");

            // 计算圆心之间的距离
            Vector3 centerToCenter = other._center - _center;
            float distance = centerToCenter.Length();

            // 检查两圆是否相交
            float sumOfRadii = _radius + other._radius;
            return distance <= sumOfRadii + tolerance;
        }

        /// <summary>
        /// 将圆转换到世界坐标系下
        /// </summary>
        /// <returns>世界坐标系下的圆</returns>
        public Circle ToWorld()
        {
            // 将平面转换到世界坐标系
            Plane worldPlane = _plane.ToWorld();

            // 将圆心转换到世界坐标系
            Vector3 worldCenter = _plane.CoordinateSystem.LocalToWorld(_center);

            return new Circle(worldPlane, worldCenter, _radius);
        }

        /// <summary>
        /// 将世界坐标系下的圆转换到当前坐标系下
        /// </summary>
        /// <param name="worldCircle">世界坐标系下的圆</param>
        /// <param name="targetSystem">目标坐标系</param>
        /// <returns>目标坐标系下的圆</returns>
        public static Circle FromWorld(Circle worldCircle, CoordinateSystem targetSystem)
        {
            // 将平面转换到目标坐标系
            Plane targetPlane = Plane.FromWorld(worldCircle.Plane, targetSystem);

            // 将圆心转换到目标坐标系
            Vector3 targetCenter = targetSystem.WorldToLocal(worldCircle.Center);

            return new Circle(targetPlane, targetCenter, worldCircle.Radius);
        }

        /// <summary>
        /// 平移圆生成新实例
        /// </summary>
        /// <param name="translation">平移向量（局部坐标）</param>
        /// <returns>平移后的新圆</returns>
        public Circle Translate(Vector3 translation)
        {
            // 圆平移后平面法向量不变，只需平移圆心
            return new Circle(_plane, _center + translation, _radius);
        }

        /// <summary>
        /// 缩放圆生成新实例
        /// </summary>
        /// <param name="scale">缩放因子</param>
        /// <returns>缩放后的新圆</returns>
        /// <exception cref="ArgumentException">当缩放因子为负数时抛出</exception>
        public Circle Scale(float scale)
        {
            if (scale < 0)
                throw new ArgumentException("缩放因子不能为负数", nameof(scale));

            return new Circle(_plane, _center, _radius * scale);
        }

        /// <summary>
        /// 旋转圆生成新实例
        /// </summary>
        /// <param name="rotationAxis">旋转轴（局部坐标，将被归一化）</param>
        /// <param name="rotationAngle">旋转角度（弧度）</param>
        /// <returns>旋转后的新圆</returns>
        public Circle Rotate(Vector3 rotationAxis, float rotationAngle)
        {
            // 旋转平面
            Plane rotatedPlane = _plane.RotateAroundPoint(_center, rotationAxis, rotationAngle);

            return new Circle(rotatedPlane, _center, _radius);
        }

        /// <summary>
        /// 返回圆的字符串表示（默认格式）
        /// </summary>
        /// <returns>包含平面、圆心和半径的字符串</returns>
        public override string ToString()
            => $"Circle [Plane: {_plane.ToString("F3")}, Center: {_center.ToString("F3")}, Radius: {_radius:F3}]";

        /// <summary>
        /// 返回圆的字符串表示（指定格式）
        /// </summary>
        /// <param name="format">数值格式字符串</param>
        /// <returns>格式化后的字符串</returns>
        public string ToString(string format)
            => $"Circle [Plane: {_plane.ToString(format)}, Center: {_center.ToString(format)}, Radius: {_radius.ToString(format)}]";

        /// <summary>
        /// 判断两个圆是否近似相等（考虑容差）
        /// </summary>
        /// <param name="other">另一个圆</param>
        /// <param name="tolerance">容差值（默认0.0001）</param>
        /// <returns>近似相等返回true，否则false</returns>
        public bool Approximately(Circle other, float tolerance = 0.0001f)
        {
            // 检查平面是否相同
            if (!_plane.Approximately(other._plane, tolerance))
                return false;

            // 检查圆心是否近似
            if (Vector3.Distance(_center, other._center) > tolerance)
                return false;

            // 检查半径是否近似
            return MathF.Abs(_radius - other._radius) < tolerance;
        }

        /// <summary>
        /// 创建圆的深拷贝
        /// </summary>
        /// <returns>新的圆实例</returns>
        public Circle Clone()
        {
            return new Circle(_plane, _center, _radius);
        }

        /// <summary>
        /// 在指定坐标系下创建XY平面上的圆
        /// </summary>
        /// <param name="coordinateSystem">坐标系</param>
        /// <param name="center">圆心（局部坐标）</param>
        /// <param name="radius">圆的半径</param>
        /// <returns>XY平面上的圆</returns>
        public static Circle CreateXYCircle(CoordinateSystem coordinateSystem, Vector3 center, float radius)
        {
            Plane xyPlane = Plane.CreateXYPlane(coordinateSystem);
            return new Circle(xyPlane, center, radius);
        }

        /// <summary>
        /// 在指定坐标系下创建YZ平面上的圆
        /// </summary>
        /// <param name="coordinateSystem">坐标系</param>
        /// <param name="center">圆心（局部坐标）</param>
        /// <param name="radius">圆的半径</param>
        /// <returns>YZ平面上的圆</returns>
        public static Circle CreateYZCircle(CoordinateSystem coordinateSystem, Vector3 center, float radius)
        {
            Plane yzPlane = Plane.CreateYZPlane(coordinateSystem);
            return new Circle(yzPlane, center, radius);
        }

        /// <summary>
        /// 在指定坐标系下创建XZ平面上的圆
        /// </summary>
        /// <param name="coordinateSystem">坐标系</param>
        /// <param name="center">圆心（局部坐标）</param>
        /// <param name="radius">圆的半径</param>
        /// <returns>XZ平面上的圆</returns>
        public static Circle CreateXZCircle(CoordinateSystem coordinateSystem, Vector3 center, float radius)
        {
            Plane xzPlane = Plane.CreateXZPlane(coordinateSystem);
            return new Circle(xzPlane, center, radius);
        }
    }
}