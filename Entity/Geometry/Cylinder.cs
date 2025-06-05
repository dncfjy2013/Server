using Entity.Geometry.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Entity.Geometry
{
    /// <summary>
    /// 表示三维空间中的圆柱，支持在指定坐标系下定义和操作
    /// 采用不可变设计，所有操作返回新的圆柱实例
    /// </summary>
    public sealed class Cylinder
    {
        /// <summary>圆柱所属的坐标系</summary>
        private readonly CoordinateSystem _coordinateSystem;

        /// <summary>圆柱底面中心（局部坐标）</summary>
        private readonly Vector3 _baseCenter;

        /// <summary>圆柱的轴向（局部坐标，已归一化）</summary>
        private readonly Vector3 _axis;

        /// <summary>圆柱的半径</summary>
        private readonly float _radius;

        /// <summary>圆柱的高度</summary>
        private readonly float _height;

        /// <summary>圆柱顶面中心（局部坐标）</summary>
        private readonly Vector3 _topCenter;

        /// <summary>圆柱所属的坐标系，只读</summary>
        public CoordinateSystem CoordinateSystem => _coordinateSystem;

        /// <summary>圆柱底面中心（局部坐标），只读</summary>
        public Vector3 BaseCenter => _baseCenter;

        /// <summary>圆柱的轴向（局部坐标，已归一化），只读</summary>
        public Vector3 Axis => _axis;

        /// <summary>圆柱的半径，只读</summary>
        public float Radius => _radius;

        /// <summary>圆柱的高度，只读</summary>
        public float Height => _height;

        /// <summary>圆柱顶面中心（局部坐标），只读</summary>
        public Vector3 TopCenter => _topCenter;

        /// <summary>
        /// 在指定坐标系下初始化圆柱
        /// </summary>
        /// <param name="coordinateSystem">圆柱所属的坐标系</param>
        /// <param name="baseCenter">圆柱底面中心（局部坐标）</param>
        /// <param name="axis">圆柱的轴向（局部坐标，将被归一化）</param>
        /// <param name="radius">圆柱的半径</param>
        /// <param name="height">圆柱的高度</param>
        /// <exception cref="ArgumentException">当半径或高度为负数，或轴向为零向量时抛出</exception>
        public Cylinder(CoordinateSystem coordinateSystem, Vector3 baseCenter, Vector3 axis, float radius, float height)
        {
            if (radius < 0)
                throw new ArgumentException("圆柱半径不能为负数", nameof(radius));

            if (height < 0)
                throw new ArgumentException("圆柱高度不能为负数", nameof(height));

            if (axis == Vector3.Zero)
                throw new ArgumentException("轴向向量不能为零向量", nameof(axis));

            _coordinateSystem = coordinateSystem;
            _baseCenter = baseCenter;
            _axis = Vector3.Normalize(axis);
            _radius = radius;
            _height = height;
            _topCenter = baseCenter + _axis * height;
        }

        /// <summary>
        /// 检查点是否在圆柱表面上（考虑容差）
        /// </summary>
        /// <param name="point">待检查的点（局部坐标）</param>
        /// <param name="tolerance">容差值（默认0.0001）</param>
        /// <returns>点在圆柱表面上返回true，否则false</returns>
        public bool ContainsPointOnSurface(Vector3 point, float tolerance = 0.0001f)
        {
            // 计算点到轴线的距离
            Vector3 pointToBase = point - _baseCenter;
            float projection = Vector3.Dot(pointToBase, _axis);
            Vector3 closestPoint = _baseCenter + _axis * projection;
            float distanceToAxis = Vector3.Distance(point, closestPoint);

            // 检查是否在侧面上
            bool onSide = MathF.Abs(distanceToAxis - _radius) < tolerance &&
                         projection >= -tolerance &&
                         projection <= _height + tolerance;

            // 检查是否在底面上
            bool onBase = MathF.Abs(projection) < tolerance &&
                         distanceToAxis <= _radius + tolerance;

            // 检查是否在顶面上
            bool onTop = MathF.Abs(projection - _height) < tolerance &&
                        distanceToAxis <= _radius + tolerance;

            return onSide || onBase || onTop;
        }

        /// <summary>
        /// 检查点是否在圆柱内部（包括边界）
        /// </summary>
        /// <param name="point">待检查的点（局部坐标）</param>
        /// <param name="tolerance">容差值（默认0.0001）</param>
        /// <returns>点在圆柱内部返回true，否则false</returns>
        public bool ContainsPointInside(Vector3 point, float tolerance = 0.0001f)
        {
            // 计算点到轴线的距离
            Vector3 pointToBase = point - _baseCenter;
            float projection = Vector3.Dot(pointToBase, _axis);
            Vector3 closestPoint = _baseCenter + _axis * projection;
            float distanceToAxis = Vector3.Distance(point, closestPoint);

            // 检查是否在圆柱内部
            return distanceToAxis <= _radius + tolerance &&
                   projection >= -tolerance &&
                   projection <= _height + tolerance;
        }

        /// <summary>
        /// 计算点到圆柱的最短距离
        /// </summary>
        /// <param name="point">待计算的点（局部坐标）</param>
        /// <returns>最短距离</returns>
        public float DistanceToPoint(Vector3 point)
        {
            // 计算点到轴线的距离和投影
            Vector3 pointToBase = point - _baseCenter;
            float projection = Vector3.Dot(pointToBase, _axis);
            Vector3 closestPoint = _baseCenter + _axis * projection;
            float distanceToAxis = Vector3.Distance(point, closestPoint);

            // 分区域处理
            if (projection < 0)
            {
                // 底面下方
                float distanceToBasePlane = Vector3.Distance(point, _baseCenter);
                return MathF.Max(0, distanceToBasePlane - _radius);
            }
            else if (projection > _height)
            {
                // 顶面上放
                float distanceToTopPlane = Vector3.Distance(point, _topCenter);
                return MathF.Max(0, distanceToTopPlane - _radius);
            }
            else
            {
                // 侧面区域
                return MathF.Max(0, distanceToAxis - _radius);
            }
        }

        /// <summary>
        /// 将点投影到圆柱表面上
        /// </summary>
        /// <param name="point">待投影的点（局部坐标）</param>
        /// <returns>投影后的点（局部坐标）</returns>
        public Vector3 ProjectPoint(Vector3 point)
        {
            // 计算点到轴线的距离和投影
            Vector3 pointToBase = point - _baseCenter;
            float projection = Vector3.Dot(pointToBase, _axis);
            Vector3 closestPoint = _baseCenter + _axis * projection;
            Vector3 direction = point - closestPoint;
            float distanceToAxis = direction.Length();

            // 处理距离为零的情况
            if (distanceToAxis < 0.000001f)
            {
                // 点在轴线上，投影到最近的底面或顶面
                if (projection < 0)
                    return _baseCenter;
                else if (projection > _height)
                    return _topCenter;
                else
                {
                    // 在轴线上且在高度范围内，投影到侧面
                    // 选择一个与轴线垂直的方向
                    Vector3 perpendicular = Vector3.Cross(_axis, Vector3.UnitX);
                    if (perpendicular.Length() < 0.000001f)
                        perpendicular = Vector3.Cross(_axis, Vector3.UnitY);
                    perpendicular = Vector3.Normalize(perpendicular);
                    return closestPoint + perpendicular * _radius;
                }
            }

            // 分区域处理
            if (projection < 0)
            {
                // 底面下方，投影到底面
                if (distanceToAxis <= _radius)
                    return _baseCenter + direction;
                else
                    return _baseCenter + direction * (_radius / distanceToAxis);
            }
            else if (projection > _height)
            {
                // 顶面上放，投影到顶面
                if (distanceToAxis <= _radius)
                    return _topCenter + direction;
                else
                    return _topCenter + direction * (_radius / distanceToAxis);
            }
            else
            {
                // 侧面区域，投影到侧面
                return closestPoint + direction * (_radius / distanceToAxis);
            }
        }

        /// <summary>
        /// 判断线段与圆柱是否相交
        /// </summary>
        /// <param name="line">线段</param>
        /// <param name="intersectionPoints">交点数组（最多两个）</param>
        /// <param name="tolerance">容差值（默认0.0001）</param>
        /// <returns>相交返回true，否则false</returns>
        public bool IntersectsLineSegment(LineSegment line, out Vector3[] intersectionPoints, float tolerance = 0.0001f)
        {
            intersectionPoints = Array.Empty<Vector3>();

            // 将线段转换到圆柱的坐标系
            LineSegment lineInCylinderSystem = line.ToCoordinateSystem(_coordinateSystem);

            Vector3 lineStart = lineInCylinderSystem.StartPoint;
            Vector3 lineEnd = lineInCylinderSystem.EndPoint;
            Vector3 lineDirection = lineEnd - lineStart;
            float lineLength = lineDirection.Length();

            if (lineLength < tolerance)
            {
                // 线段退化为点
                if (ContainsPointInside(lineStart, tolerance))
                {
                    intersectionPoints = new[] { lineStart };
                    return true;
                }
                return false;
            }

            lineDirection /= lineLength;

            // 计算线段与圆柱侧面的交点
            Vector3 d = lineDirection;
            Vector3 p = lineStart - _baseCenter;

            // 计算与轴线垂直的平面上的投影
            Vector3 dPerp = d - Vector3.Dot(d, _axis) * _axis;
            Vector3 pPerp = p - Vector3.Dot(p, _axis) * _axis;

            // 解二次方程：a*t^2 + b*t + c = 0
            float a = Vector3.Dot(dPerp, dPerp);
            float b = 2 * Vector3.Dot(pPerp, dPerp);
            float c = Vector3.Dot(pPerp, pPerp) - _radius * _radius;

            float discriminant = b * b - 4 * a * c;
            if (discriminant < 0)
            {
                // 与侧面无交点，检查与底面和顶面的交点
                return IntersectsLineSegmentWithCaps(lineInCylinderSystem, out intersectionPoints, tolerance);
            }

            float sqrtDiscriminant = MathF.Sqrt(discriminant);
            float t1 = (-b + sqrtDiscriminant) / (2 * a);
            float t2 = (-b - sqrtDiscriminant) / (2 * a);

            // 检查交点是否在线段范围内和圆柱高度范围内
            bool hasT1 = false, hasT2 = false;
            Vector3 point1 = Vector3.Zero, point2 = Vector3.Zero;

            if (t1 >= 0 && t1 <= lineLength)
            {
                point1 = lineStart + d * t1;
                float projection = Vector3.Dot(point1 - _baseCenter, _axis);
                if (projection >= -tolerance && projection <= _height + tolerance)
                {
                    hasT1 = true;
                }
            }

            if (t2 >= 0 && t2 <= lineLength)
            {
                point2 = lineStart + d * t2;
                float projection = Vector3.Dot(point2 - _baseCenter, _axis);
                if (projection >= -tolerance && projection <= _height + tolerance)
                {
                    hasT2 = true;
                }
            }

            // 检查与底面和顶面的交点
            Vector3[] capIntersections;
            bool hasCapIntersections = IntersectsLineSegmentWithCaps(lineInCylinderSystem, out capIntersections, tolerance);

            // 合并交点
            int count = 0;
            if (hasT1) count++;
            if (hasT2) count++;
            if (hasCapIntersections) count += capIntersections.Length;

            if (count > 0)
            {
                intersectionPoints = new Vector3[count];
                int index = 0;
                if (hasT1) intersectionPoints[index++] = point1;
                if (hasT2) intersectionPoints[index++] = point2;
                if (hasCapIntersections)
                {
                    foreach (var point in capIntersections)
                    {
                        // 过滤重复点
                        bool isDuplicate = false;
                        for (int i = 0; i < index; i++)
                        {
                            if (Vector3.Distance(intersectionPoints[i], point) < tolerance)
                            {
                                isDuplicate = true;
                                break;
                            }
                        }
                        if (!isDuplicate)
                            intersectionPoints[index++] = point;
                    }
                }
                return true;
            }

            return false;
        }

        /// <summary>
        /// 判断线段与圆柱底面和顶面的交点
        /// </summary>
        private bool IntersectsLineSegmentWithCaps(LineSegment line, out Vector3[] intersectionPoints, float tolerance = 0.0001f)
        {
            intersectionPoints = Array.Empty<Vector3>();
            int count = 0;
            Vector3[] points = new Vector3[2];

            // 创建底面和顶面的平面
            Plane basePlane = new Plane(_coordinateSystem, _baseCenter, -_axis);
            Plane topPlane = new Plane(_coordinateSystem, _topCenter, _axis);

            // 检查与底面的交点
            if (line.IntersectsPlane(basePlane, out Vector3 baseIntersection, tolerance))
            {
                Vector3 toIntersection = baseIntersection - _baseCenter;
                float distanceSquared = toIntersection.LengthSquared();
                if (distanceSquared <= _radius * _radius + tolerance)
                {
                    points[count++] = baseIntersection;
                }
            }

            // 检查与顶面的交点
            if (line.IntersectsPlane(topPlane, out Vector3 topIntersection, tolerance))
            {
                Vector3 toIntersection = topIntersection - _topCenter;
                float distanceSquared = toIntersection.LengthSquared();
                if (distanceSquared <= _radius * _radius + tolerance)
                {
                    points[count++] = topIntersection;
                }
            }

            if (count > 0)
            {
                intersectionPoints = new Vector3[count];
                Array.Copy(points, intersectionPoints, count);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 判断圆柱与球是否相交
        /// </summary>
        /// <param name="sphere">球</param>
        /// <param name="tolerance">容差值（默认0.0001）</param>
        /// <returns>相交返回true，否则false</returns>
        public bool IntersectsSphere(Sphere sphere, float tolerance = 0.0001f)
        {
            // 将球转换到圆柱的坐标系
            Sphere sphereInCylinderSystem = sphere.ToCoordinateSystem(_coordinateSystem);

            Vector3 sphereCenter = sphereInCylinderSystem.Center;
            float sphereRadius = sphereInCylinderSystem.Radius;

            // 计算球心到圆柱轴线的距离和投影
            Vector3 centerToBase = sphereCenter - _baseCenter;
            float projection = Vector3.Dot(centerToBase, _axis);
            Vector3 closestPoint = _baseCenter + _axis * projection;
            float distanceToAxis = Vector3.Distance(sphereCenter, closestPoint);

            // 检查球与圆柱侧面的距离
            float distanceToSide = MathF.Max(0, distanceToAxis - _radius);

            // 检查球与底面和顶面的距离
            float distanceToBase = MathF.Max(0, -projection);
            float distanceToTop = MathF.Max(0, projection - _height);

            // 如果球心在圆柱高度范围内，检查与侧面的距离
            if (projection >= -tolerance && projection <= _height + tolerance)
            {
                return distanceToSide <= sphereRadius + tolerance;
            }
            // 如果球心在底面下方，检查与底面的距离
            else if (projection < 0)
            {
                // 计算到圆柱底面圆的距离
                float distanceToBaseCircle = MathF.Sqrt(distanceToBase * distanceToBase + distanceToSide * distanceToSide);
                return distanceToBaseCircle <= sphereRadius + tolerance;
            }
            // 如果球心在顶面上放，检查与顶面的距离
            else
            {
                // 计算到圆柱顶面圆的距离
                float distanceToTopCircle = MathF.Sqrt(distanceToTop * distanceToTop + distanceToSide * distanceToSide);
                return distanceToTopCircle <= sphereRadius + tolerance;
            }
        }

        /// <summary>
        /// 将圆柱转换到世界坐标系下
        /// </summary>
        /// <returns>世界坐标系下的圆柱</returns>
        public Cylinder ToWorld()
        {
            // 将底面中心和顶面中心转换到世界坐标系
            Vector3 worldBaseCenter = _coordinateSystem.LocalToWorld(_baseCenter);
            Vector3 worldTopCenter = _coordinateSystem.LocalToWorld(_topCenter);

            // 计算世界坐标系下的轴向
            Vector3 worldAxis = worldTopCenter - worldBaseCenter;

            return new Cylinder(new CoordinateSystem(), worldBaseCenter, worldAxis, _radius, _height);
        }

        /// <summary>
        /// 将圆柱转换到指定坐标系下
        /// </summary>
        /// <param name="targetSystem">目标坐标系</param>
        /// <returns>目标坐标系下的圆柱</returns>
        public Cylinder ToCoordinateSystem(CoordinateSystem targetSystem)
        {
            // 将底面中心和顶面中心转换到目标坐标系
            Vector3 targetBaseCenter = targetSystem.WorldToLocal(_coordinateSystem.LocalToWorld(_baseCenter));
            Vector3 targetTopCenter = targetSystem.WorldToLocal(_coordinateSystem.LocalToWorld(_topCenter));

            // 计算目标坐标系下的轴向
            Vector3 targetAxis = targetTopCenter - targetBaseCenter;

            return new Cylinder(targetSystem, targetBaseCenter, targetAxis, _radius, _height);
        }

        /// <summary>
        /// 平移圆柱生成新实例
        /// </summary>
        /// <param name="translation">平移向量（局部坐标）</param>
        /// <returns>平移后的新圆柱</returns>
        public Cylinder Translate(Vector3 translation)
        {
            return new Cylinder(_coordinateSystem, _baseCenter + translation, _axis, _radius, _height);
        }

        /// <summary>
        /// 缩放圆柱生成新实例
        /// </summary>
        /// <param name="scale">缩放因子</param>
        /// <returns>缩放后的新圆柱</returns>
        /// <exception cref="ArgumentException">当缩放因子为负数时抛出</exception>
        public Cylinder Scale(float scale)
        {
            if (scale < 0)
                throw new ArgumentException("缩放因子不能为负数", nameof(scale));

            return new Cylinder(_coordinateSystem, _baseCenter, _axis, _radius * scale, _height * scale);
        }

        /// <summary>
        /// 旋转圆柱生成新实例
        /// </summary>
        /// <param name="rotationAxis">旋转轴（局部坐标，将被归一化）</param>
        /// <param name="rotationAngle">旋转角度（弧度）</param>
        /// <returns>旋转后的新圆柱</returns>
        public Cylinder Rotate(Vector3 rotationAxis, float rotationAngle)
        {
            if (rotationAxis == Vector3.Zero)
                return this;

            // 创建旋转矩阵
            Matrix4x4 rotationMatrix = Matrix4x4.CreateFromAxisAngle(rotationAxis, rotationAngle);

            // 旋转底面中心和顶面中心
            Vector3 rotatedBaseCenter = Vector3.Transform(_baseCenter, rotationMatrix);
            Vector3 rotatedTopCenter = Vector3.Transform(_topCenter, rotationMatrix);

            // 计算新的轴向
            Vector3 newAxis = rotatedTopCenter - rotatedBaseCenter;

            return new Cylinder(_coordinateSystem, rotatedBaseCenter, newAxis, _radius, _height);
        }

        /// <summary>
        /// 返回圆柱的字符串表示（默认格式）
        /// </summary>
        /// <returns>包含坐标系、底面中心、轴向、半径和高度的字符串</returns>
        public override string ToString()
            => $"Cylinder [System: {_coordinateSystem.ToString("F3")}, BaseCenter: {_baseCenter.ToString("F3")}, Axis: {_axis.ToString("F3")}, Radius: {_radius:F3}, Height: {_height:F3}]";

        /// <summary>
        /// 返回圆柱的字符串表示（指定格式）
        /// </summary>
        /// <param name="format">数值格式字符串</param>
        /// <returns>格式化后的字符串</returns>
        public string ToString(string format)
            => $"Cylinder [System: {_coordinateSystem.ToString(format)}, BaseCenter: {_baseCenter.ToString(format)}, Axis: {_axis.ToString(format)}, Radius: {_radius.ToString(format)}, Height: {_height.ToString(format)}]";

        /// <summary>
        /// 判断两个圆柱是否近似相等（考虑容差）
        /// </summary>
        /// <param name="other">另一个圆柱</param>
        /// <param name="tolerance">容差值（默认0.0001）</param>
        /// <returns>近似相等返回true，否则false</returns>
        public bool Approximately(Cylinder other, float tolerance = 0.0001f)
        {
            // 检查坐标系是否相同
            if (!_coordinateSystem.Approximately(other._coordinateSystem, tolerance))
                return false;

            // 检查底面中心是否近似相等
            if (Vector3.Distance(_baseCenter, other._baseCenter) > tolerance)
                return false;

            // 检查轴向是否近似相等（允许方向相反）
            float dot = MathF.Abs(Vector3.Dot(_axis, other._axis));
            if (dot < 1 - tolerance)
                return false;

            // 检查半径和高度是否近似相等
            if (MathF.Abs(_radius - other._radius) > tolerance)
                return false;

            return MathF.Abs(_height - other._height) <= tolerance;
        }

        /// <summary>
        /// 创建圆柱的深拷贝
        /// </summary>
        /// <returns>新的圆柱实例</returns>
        public Cylinder Clone()
        {
            return new Cylinder(_coordinateSystem, _baseCenter, _axis, _radius, _height);
        }

        /// <summary>
        /// 获取圆柱的底面平面
        /// </summary>
        /// <returns>底面平面</returns>
        public Plane GetBasePlane()
        {
            return new Plane(_coordinateSystem, _baseCenter, -_axis);
        }

        /// <summary>
        /// 获取圆柱的顶面平面
        /// </summary>
        /// <returns>顶面平面</returns>
        public Plane GetTopPlane()
        {
            return new Plane(_coordinateSystem, _topCenter, _axis);
        }
    }
}
