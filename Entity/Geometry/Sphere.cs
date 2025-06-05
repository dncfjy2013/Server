using Entity.Geometry.Common;
using System;
using System.Numerics;

namespace Entity.Geometry
{
    /// <summary>
    /// 表示三维空间中的球体，支持在指定坐标系下定义和操作
    /// 采用不可变设计，所有操作返回新的球体实例
    /// </summary>
    public sealed class Sphere
    {
        /// <summary>球体所属的坐标系</summary>
        private readonly CoordinateSystem _coordinateSystem;

        /// <summary>球心（局部坐标）</summary>
        private readonly Vector3 _center;

        /// <summary>球的半径</summary>
        private readonly float _radius;

        /// <summary>球体所属的坐标系，只读</summary>
        public CoordinateSystem CoordinateSystem => _coordinateSystem;

        /// <summary>球心（局部坐标），只读</summary>
        public Vector3 Center => _center;

        /// <summary>球的半径，只读</summary>
        public float Radius => _radius;

        /// <summary>球的表面积</summary>
        public float SurfaceArea => 4 * MathF.PI * _radius * _radius;

        /// <summary>球的体积</summary>
        public float Volume => (4f / 3f) * MathF.PI * _radius * _radius * _radius;

        /// <summary>
        /// 在指定坐标系下初始化球体
        /// </summary>
        /// <param name="coordinateSystem">球体所属的坐标系</param>
        /// <param name="center">球心（局部坐标）</param>
        /// <param name="radius">球的半径</param>
        /// <exception cref="ArgumentException">当半径为负数时抛出</exception>
        public Sphere(CoordinateSystem coordinateSystem, Vector3 center, float radius)
        {
            if (radius < 0)
                throw new ArgumentException("球的半径不能为负数", nameof(radius));

            _coordinateSystem = coordinateSystem;
            _center = center;
            _radius = radius;
        }

        /// <summary>
        /// 检查点是否在球面上（考虑容差）
        /// </summary>
        /// <param name="point">待检查的点（局部坐标）</param>
        /// <param name="tolerance">容差值（默认0.0001）</param>
        /// <returns>点在球面上返回true，否则false</returns>
        public bool ContainsPointOnSurface(Vector3 point, float tolerance = 0.0001f)
        {
            // 计算点到球心的距离
            Vector3 centerToPoint = point - _center;
            float distance = centerToPoint.Length();

            // 检查距离是否近似等于半径
            return MathF.Abs(distance - _radius) < tolerance;
        }

        /// <summary>
        /// 检查点是否在球内部（包括边界）
        /// </summary>
        /// <param name="point">待检查的点（局部坐标）</param>
        /// <param name="tolerance">容差值（默认0.0001）</param>
        /// <returns>点在球内部返回true，否则false</returns>
        public bool ContainsPointInside(Vector3 point, float tolerance = 0.0001f)
        {
            // 计算点到球心的距离平方
            Vector3 centerToPoint = point - _center;
            float distanceSquared = centerToPoint.LengthSquared();

            // 检查距离平方是否小于等于半径平方（考虑容差）
            return distanceSquared <= (_radius * _radius) + tolerance;
        }

        /// <summary>
        /// 计算点到球的最短距离
        /// </summary>
        /// <param name="point">待计算的点（局部坐标）</param>
        /// <returns>最短距离</returns>
        public float DistanceToPoint(Vector3 point)
        {
            // 计算点到球心的距离
            Vector3 centerToPoint = point - _center;
            float distanceToCenter = centerToPoint.Length();

            // 最短距离为距离减去半径（如果点在球外）
            // 否则为0（点在球内或球上）
            return MathF.Max(0, distanceToCenter - _radius);
        }

        /// <summary>
        /// 将点投影到球面上
        /// 如果点在球内部，返回球心方向的球面点；否则返回点在球面上的投影
        /// </summary>
        /// <param name="point">待投影的点（局部坐标）</param>
        /// <returns>投影后的点（局部坐标）</returns>
        public Vector3 ProjectPoint(Vector3 point)
        {
            // 计算点到球心的向量
            Vector3 centerToPoint = point - _center;
            float distanceToCenter = centerToPoint.Length();

            // 如果点在球心，返回任意球面上的点（如X轴方向）
            if (distanceToCenter < 0.0001f)
                return _center + _coordinateSystem.XAxis * _radius;

            // 归一化向量并乘以半径
            return _center + Vector3.Normalize(centerToPoint) * _radius;
        }

        /// <summary>
        /// 判断直线与球是否相交
        /// </summary>
        /// <param name="lineStart">直线起点（局部坐标）</param>
        /// <param name="lineDirection">直线方向向量（局部坐标）</param>
        /// <param name="intersectionPoints">交点数组（最多两个）</param>
        /// <param name="tolerance">容差值（默认0.0001）</param>
        /// <returns>相交返回true，否则false</returns>
        public bool IntersectsLine(Vector3 lineStart, Vector3 lineDirection, out Vector3[] intersectionPoints, float tolerance = 0.0001f)
        {
            intersectionPoints = Array.Empty<Vector3>();

            // 计算直线与球心的距离
            Vector3 lineToCenter = _center - lineStart;
            float lineDirectionLength = lineDirection.Length();

            // 归一化方向向量以简化计算
            if (lineDirectionLength < tolerance)
                return false;

            Vector3 normalizedDirection = lineDirection / lineDirectionLength;
            float t = Vector3.Dot(lineToCenter, normalizedDirection);
            float dSquared = Vector3.Dot(lineToCenter, lineToCenter) - t * t;

            // 计算判别式
            float rSquared = _radius * _radius;
            if (dSquared > rSquared + tolerance)
                return false; // 直线与球不相交

            float s = MathF.Sqrt(rSquared - dSquared);
            float t1 = t - s;
            float t2 = t + s;

            // 计算交点
            intersectionPoints = new Vector3[2];
            intersectionPoints[0] = lineStart + normalizedDirection * t1;
            intersectionPoints[1] = lineStart + normalizedDirection * t2;

            // 如果两个交点重合，返回一个点
            if (MathF.Abs(t1 - t2) < tolerance)
                Array.Resize(ref intersectionPoints, 1);

            return true;
        }

        /// <summary>
        /// 将球体转换到指定坐标系下
        /// </summary>
        /// <param name="targetSystem">目标坐标系</param>
        /// <returns>目标坐标系下的球体</returns>
        public Sphere ToCoordinateSystem(CoordinateSystem targetSystem)
        {
            // 将球心从当前坐标系转换到世界坐标系，再从世界坐标系转换到目标坐标系
            Vector3 targetCenter = targetSystem.WorldToLocal(_coordinateSystem.LocalToWorld(_center));

            return new Sphere(targetSystem, targetCenter, _radius);
        }

        /// <summary>
        /// 判断线段与球是否相交
        /// </summary>
        /// <param name="lineStart">线段起点（局部坐标）</param>
        /// <param name="lineEnd">线段终点（局部坐标）</param>
        /// <param name="intersectionPoints">交点数组（最多两个）</param>
        /// <param name="tolerance">容差值（默认0.0001）</param>
        /// <returns>相交返回true，否则false</returns>
        public bool IntersectsLineSegment(Vector3 lineStart, Vector3 lineEnd, out Vector3[] intersectionPoints, float tolerance = 0.0001f)
        {
            intersectionPoints = Array.Empty<Vector3>();

            // 计算线段方向向量
            Vector3 direction = lineEnd - lineStart;
            float directionLength = direction.Length();

            // 处理线段退化为点的情况
            if (directionLength < tolerance)
            {
                if (ContainsPointInside(lineStart, tolerance))
                {
                    intersectionPoints = new[] { lineStart };
                    return true;
                }
                return false;
            }

            // 调用直线相交方法
            if (!IntersectsLine(lineStart, direction, out Vector3[] lineIntersections, tolerance))
                return false;

            // 检查交点是否在线段范围内
            Vector3 normalizedDirection = direction / directionLength;
            int validCount = 0;

            foreach (var point in lineIntersections)
            {
                Vector3 diff = point - lineStart;
                float t = Vector3.Dot(diff, normalizedDirection);

                if (t >= -tolerance && t <= directionLength + tolerance)
                {
                    intersectionPoints[validCount++] = point;
                    if (validCount >= lineIntersections.Length)
                        break;
                }
            }

            if (validCount > 0)
            {
                Array.Resize(ref intersectionPoints, validCount);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 判断平面与球是否相交
        /// </summary>
        /// <param name="plane">平面</param>
        /// <param name="intersectionCircle">相交圆（如果相交）</param>
        /// <param name="tolerance">容差值（默认0.0001）</param>
        /// <returns>相交返回true，否则false</returns>
        public bool IntersectsPlane(Plane plane, out Circle intersectionCircle, float tolerance = 0.0001f)
        {
            intersectionCircle = null;

            // 计算球心到平面的距离
            float distance = plane.DistanceToPoint(_center);

            // 平面与球不相交
            if (distance > _radius + tolerance)
                return false;

            // 平面与球相切
            if (MathF.Abs(distance - _radius) < tolerance)
                return true;

            // 平面与球相交，计算相交圆
            float circleRadius = MathF.Sqrt(_radius * _radius - distance * distance);
            Vector3 circleCenter = _center - plane.Normal * distance;

            intersectionCircle = new Circle(plane, circleCenter, circleRadius);
            return true;
        }

        /// <summary>
        /// 判断两个球是否相交
        /// </summary>
        /// <param name="other">另一个球</param>
        /// <param name="tolerance">容差值（默认0.0001）</param>
        /// <returns>相交返回true，否则false</returns>
        public bool IntersectsSphere(Sphere other, float tolerance = 0.0001f)
        {
            // 计算球心之间的距离
            Vector3 centerToCenter = other._center - _center;
            float distance = centerToCenter.Length();

            // 检查两球是否相交
            float sumOfRadii = _radius + other._radius;
            return distance <= sumOfRadii + tolerance;
        }

        /// <summary>
        /// 将球转换到世界坐标系下
        /// </summary>
        /// <returns>世界坐标系下的球</returns>
        public Sphere ToWorld()
        {
            // 将球心转换到世界坐标系
            Vector3 worldCenter = _coordinateSystem.LocalToWorld(_center);

            return new Sphere(new CoordinateSystem(), worldCenter, _radius);
        }

        /// <summary>
        /// 将世界坐标系下的球转换到当前坐标系下
        /// </summary>
        /// <param name="worldSphere">世界坐标系下的球</param>
        /// <returns>当前坐标系下的球</returns>
        public static Sphere FromWorld(Sphere worldSphere, CoordinateSystem targetSystem)
        {
            // 将球心转换到目标坐标系
            Vector3 targetCenter = targetSystem.WorldToLocal(worldSphere.Center);

            return new Sphere(targetSystem, targetCenter, worldSphere.Radius);
        }

        /// <summary>
        /// 平移球生成新实例
        /// </summary>
        /// <param name="translation">平移向量（局部坐标）</param>
        /// <returns>平移后的新球</returns>
        public Sphere Translate(Vector3 translation)
        {
            // 球平移后半径不变，只需平移球心
            return new Sphere(_coordinateSystem, _center + translation, _radius);
        }

        /// <summary>
        /// 缩放球生成新实例
        /// </summary>
        /// <param name="scale">缩放因子</param>
        /// <returns>缩放后的新球</returns>
        /// <exception cref="ArgumentException">当缩放因子为负数时抛出</exception>
        public Sphere Scale(float scale)
        {
            if (scale < 0)
                throw new ArgumentException("缩放因子不能为负数", nameof(scale));

            return new Sphere(_coordinateSystem, _center, _radius * scale);
        }

        /// <summary>
        /// 旋转球生成新实例
        /// 注意：旋转球不会改变其形状，仅改变球心位置
        /// </summary>
        /// <param name="rotationAxis">旋转轴（局部坐标，将被归一化）</param>
        /// <param name="rotationAngle">旋转角度（弧度）</param>
        /// <returns>旋转后的新球</returns>
        public Sphere Rotate(Vector3 rotationAxis, float rotationAngle)
        {
            if (rotationAxis == Vector3.Zero)
                return this; // 零向量旋转不改变球

            // 将旋转轴转换到世界坐标
            Vector3 worldAxis = _coordinateSystem.LocalToWorldDirection(rotationAxis);
            worldAxis = Vector3.Normalize(worldAxis);

            // 创建绕世界轴的旋转矩阵
            Matrix4x4 rotationMatrix = Matrix4x4.CreateFromAxisAngle(worldAxis, rotationAngle);

            // 旋转球心（相对于坐标系原点）
            Vector3 worldCenter = _coordinateSystem.LocalToWorld(_center);
            Vector3 rotatedWorldCenter = Vector3.Transform(worldCenter, rotationMatrix);
            Vector3 rotatedLocalCenter = _coordinateSystem.WorldToLocal(rotatedWorldCenter);

            return new Sphere(_coordinateSystem, rotatedLocalCenter, _radius);
        }

        /// <summary>
        /// 返回球的字符串表示（默认格式）
        /// </summary>
        /// <returns>包含坐标系、球心和半径的字符串</returns>
        public override string ToString()
            => $"Sphere [System: {_coordinateSystem.ToString("F3")}, Center: {_center.ToString("F3")}, Radius: {_radius:F3}]";

        /// <summary>
        /// 返回球的字符串表示（指定格式）
        /// </summary>
        /// <param name="format">数值格式字符串</param>
        /// <returns>格式化后的字符串</returns>
        public string ToString(string format)
            => $"Sphere [System: {_coordinateSystem.ToString(format)}, Center: {_center.ToString(format)}, Radius: {_radius.ToString(format)}]";

        /// <summary>
        /// 判断两个球是否近似相等（考虑容差）
        /// </summary>
        /// <param name="other">另一个球</param>
        /// <param name="tolerance">容差值（默认0.0001）</param>
        /// <returns>近似相等返回true，否则false</returns>
        public bool Approximately(Sphere other, float tolerance = 0.0001f)
        {
            // 检查坐标系是否相同
            if (!_coordinateSystem.Approximately(other._coordinateSystem, tolerance))
                return false;

            // 检查球心是否近似
            if (Vector3.Distance(_center, other._center) > tolerance)
                return false;

            // 检查半径是否近似
            return MathF.Abs(_radius - other._radius) < tolerance;
        }

        /// <summary>
        /// 创建球的深拷贝
        /// </summary>
        /// <returns>新的球实例</returns>
        public Sphere Clone()
        {
            return new Sphere(_coordinateSystem, _center, _radius);
        }

        /// <summary>
        /// 在指定坐标系下创建单位球（球心在原点，半径为1）
        /// </summary>
        /// <param name="coordinateSystem">坐标系</param>
        /// <returns>单位球</returns>
        public static Sphere CreateUnitSphere(CoordinateSystem coordinateSystem)
            => new Sphere(coordinateSystem, Vector3.Zero, 1);

        /// <summary>
        /// 在指定坐标系下创建球
        /// </summary>
        /// <param name="coordinateSystem">坐标系</param>
        /// <param name="center">球心（局部坐标）</param>
        /// <param name="radius">球的半径</param>
        /// <returns>指定的球</returns>
        public static Sphere Create(CoordinateSystem coordinateSystem, Vector3 center, float radius)
            => new Sphere(coordinateSystem, center, radius);
    }
}