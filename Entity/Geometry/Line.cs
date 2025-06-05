using Entity.Geometry.Common;
using System;
using System.Numerics;

namespace Entity.Geometry
{
    /// <summary>
    /// 表示三维空间中的线段，支持在指定坐标系下定义和操作
    /// 采用不可变设计，所有操作返回新的线段实例
    /// </summary>
    public sealed class LineSegment
    {
        /// <summary>线段所属的坐标系</summary>
        private readonly CoordinateSystem _coordinateSystem;

        /// <summary>线段起点（局部坐标）</summary>
        private readonly Vector3 _startPoint;

        /// <summary>线段终点（局部坐标）</summary>
        private readonly Vector3 _endPoint;

        /// <summary>线段的方向向量（从起点到终点，未归一化）</summary>
        private readonly Vector3 _direction;

        /// <summary>线段的长度</summary>
        private readonly float _length;

        /// <summary>线段所属的坐标系，只读</summary>
        public CoordinateSystem CoordinateSystem => _coordinateSystem;

        /// <summary>线段起点（局部坐标），只读</summary>
        public Vector3 StartPoint => _startPoint;

        /// <summary>线段终点（局部坐标），只读</summary>
        public Vector3 EndPoint => _endPoint;

        /// <summary>线段的方向向量（从起点到终点，已归一化），只读</summary>
        public Vector3 Direction => _length > 0 ? _direction / _length : Vector3.Zero;

        /// <summary>线段的长度，只读</summary>
        public float Length => _length;

        /// <summary>线段长度的平方，只读</summary>
        public float LengthSquared => _length * _length;

        /// <summary>
        /// 在指定坐标系下初始化线段
        /// </summary>
        /// <param name="coordinateSystem">线段所属的坐标系</param>
        /// <param name="startPoint">线段起点（局部坐标）</param>
        /// <param name="endPoint">线段终点（局部坐标）</param>
        public LineSegment(CoordinateSystem coordinateSystem, Vector3 startPoint, Vector3 endPoint)
        {
            _coordinateSystem = coordinateSystem;
            _startPoint = startPoint;
            _endPoint = endPoint;
            _direction = endPoint - startPoint;
            _length = _direction.Length();
        }

        /// <summary>
        /// 使用起点、方向和长度在指定坐标系下初始化线段
        /// </summary>
        /// <param name="coordinateSystem">线段所属的坐标系</param>
        /// <param name="startPoint">线段起点（局部坐标）</param>
        /// <param name="direction">线段方向（局部坐标，将被归一化）</param>
        /// <param name="length">线段长度</param>
        /// <exception cref="ArgumentException">当长度为负数或方向向量为零向量时抛出</exception>
        public LineSegment(CoordinateSystem coordinateSystem, Vector3 startPoint, Vector3 direction, float length)
        {
            if (length < 0)
                throw new ArgumentException("线段长度不能为负数", nameof(length));

            if (direction == Vector3.Zero)
                throw new ArgumentException("方向向量不能为零向量", nameof(direction));

            _coordinateSystem = coordinateSystem;
            _startPoint = startPoint;
            _length = length;
            _direction = Vector3.Normalize(direction) * length;
            _endPoint = startPoint + _direction;
        }

        /// <summary>
        /// 检查点是否在线段上（考虑容差）
        /// </summary>
        /// <param name="point">待检查的点（局部坐标）</param>
        /// <param name="tolerance">容差值（默认0.0001）</param>
        /// <returns>点在线段上返回true，否则false</returns>
        public bool ContainsPoint(Vector3 point, float tolerance = 0.0001f)
        {
            // 计算点到线段的距离
            float distance = DistanceToPoint(point);
            return distance < tolerance;
        }

        /// <summary>
        /// 计算点到线段的最短距离
        /// </summary>
        /// <param name="point">待计算的点（局部坐标）</param>
        /// <returns>最短距离</returns>
        public float DistanceToPoint(Vector3 point)
        {
            // 计算线段的方向向量
            Vector3 direction = _endPoint - _startPoint;
            float lengthSquared = direction.LengthSquared();

            // 如果线段退化为一个点，返回点到该点的距离
            if (lengthSquared < 0.000001f)
                return Vector3.Distance(point, _startPoint);

            // 计算投影比例t
            Vector3 startToPoint = point - _startPoint;
            float t = MathF.Max(0, MathF.Min(1, Vector3.Dot(startToPoint, direction) / lengthSquared));

            // 计算投影点
            Vector3 projection = _startPoint + t * direction;

            // 返回点到投影点的距离
            return Vector3.Distance(point, projection);
        }

        /// <summary>
        /// 将点投影到线段上
        /// </summary>
        /// <param name="point">待投影的点（局部坐标）</param>
        /// <returns>投影后的点（局部坐标）</returns>
        public Vector3 ProjectPoint(Vector3 point)
        {
            // 计算线段的方向向量
            Vector3 direction = _endPoint - _startPoint;
            float lengthSquared = direction.LengthSquared();

            // 如果线段退化为一个点，返回该点
            if (lengthSquared < 0.000001f)
                return _startPoint;

            // 计算投影比例t
            Vector3 startToPoint = point - _startPoint;
            float t = MathF.Max(0, MathF.Min(1, Vector3.Dot(startToPoint, direction) / lengthSquared));

            // 返回投影点
            return _startPoint + t * direction;
        }

        /// <summary>
        /// 判断线段与平面是否相交
        /// </summary>
        /// <param name="plane">平面</param>
        /// <param name="intersectionPoint">交点（如果相交）</param>
        /// <param name="tolerance">容差值（默认0.0001）</param>
        /// <returns>相交返回true，否则false</returns>
        public bool IntersectsPlane(Plane plane, out Vector3 intersectionPoint, float tolerance = 0.0001f)
        {
            intersectionPoint = Vector3.Zero;

            // 计算线段起点和终点到平面的有符号距离
            float d1 = plane.DistanceToPoint(_startPoint);
            float d2 = plane.DistanceToPoint(_endPoint);

            // 如果两个端点在平面同一侧，不相交
            if (d1 * d2 > tolerance)
                return false;

            // 如果两个端点都在平面上，返回起点
            if (MathF.Abs(d1) < tolerance && MathF.Abs(d2) < tolerance)
            {
                intersectionPoint = _startPoint;
                return true;
            }

            // 计算交点参数t
            float t = d1 / (d1 - d2);
            intersectionPoint = _startPoint + t * (_endPoint - _startPoint);
            return true;
        }

        /// <summary>
        /// 判断两条线段是否相交
        /// </summary>
        /// <param name="other">另一条线段</param>
        /// <param name="intersectionPoint">交点（如果相交）</param>
        /// <param name="tolerance">容差值（默认0.0001）</param>
        /// <returns>相交返回true，否则false</returns>
        public bool IntersectsLineSegment(LineSegment other, out Vector3 intersectionPoint, float tolerance = 0.0001f)
        {
            intersectionPoint = Vector3.Zero;

            // 将另一条线段转换到当前坐标系
            LineSegment otherInThisSystem = other.ToCoordinateSystem(_coordinateSystem);

            Vector3 p1 = _startPoint;
            Vector3 p2 = _endPoint;
            Vector3 p3 = otherInThisSystem._startPoint;
            Vector3 p4 = otherInThisSystem._endPoint;

            Vector3 d1 = p2 - p1;
            Vector3 d2 = p4 - p3;
            Vector3 r = p1 - p3;

            // 计算叉积
            Vector3 crossD1D2 = Vector3.Cross(d1, d2);
            float denominator = crossD1D2.LengthSquared();

            // 如果分母接近零，表示线段平行或共线
            if (denominator < tolerance)
            {
                // 检查是否共线
                Vector3 crossRDi = Vector3.Cross(r, d1);
                if (crossRDi.LengthSquared() < tolerance)
                {
                    // 共线，检查是否重叠
                    // 计算参数t1和t2
                    float t1min = 0, t1max = 1;
                    float t2min = 0, t2max = 1;

                    // 检查x方向
                    if (MathF.Abs(d1.X) > tolerance)
                    {
                        t2min = MathF.Max(t2min, (p1.X - p3.X) / d1.X);
                        t2max = MathF.Min(t2max, (p2.X - p3.X) / d1.X);
                    }
                    // 检查y方向
                    if (MathF.Abs(d1.Y) > tolerance)
                    {
                        t2min = MathF.Max(t2min, (p1.Y - p3.Y) / d1.Y);
                        t2max = MathF.Min(t2max, (p2.Y - p3.Y) / d1.Y);
                    }
                    // 检查z方向
                    if (MathF.Abs(d1.Z) > tolerance)
                    {
                        t2min = MathF.Max(t2min, (p1.Z - p3.Z) / d1.Z);
                        t2max = MathF.Min(t2max, (p2.Z - p3.Z) / d1.Z);
                    }

                    if (t2min <= t2max + tolerance)
                    {
                        // 线段重叠，返回中点作为交点
                        float te = (t2min + t2max) / 2;
                        intersectionPoint = p3 + te * d2;
                        return true;
                    }
                }
                return false;
            }

            // 计算参数s和t
            float s = Vector3.Dot(Vector3.Cross(r, d2), crossD1D2) / denominator;
            float t = Vector3.Dot(Vector3.Cross(r, d1), crossD1D2) / denominator;

            // 检查参数是否在[0,1]范围内
            if (s >= -tolerance && s <= 1 + tolerance && t >= -tolerance && t <= 1 + tolerance)
            {
                intersectionPoint = p1 + s * d1;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 判断线段与球是否相交
        /// </summary>
        /// <param name="sphere">球</param>
        /// <param name="intersectionPoints">交点数组（最多两个）</param>
        /// <param name="tolerance">容差值（默认0.0001）</param>
        /// <returns>相交返回true，否则false</returns>
        public bool IntersectsSphere(Sphere sphere, out Vector3[] intersectionPoints, float tolerance = 0.0001f)
        {
            intersectionPoints = Array.Empty<Vector3>();

            // 将球转换到当前坐标系
            Sphere sphereInThisSystem = sphere.ToCoordinateSystem(_coordinateSystem);

            Vector3 center = sphereInThisSystem.Center;
            float radius = sphereInThisSystem.Radius;

            Vector3 d = _endPoint - _startPoint;
            Vector3 m = _startPoint - center;

            float a = Vector3.Dot(d, d);
            float b = 2 * Vector3.Dot(m, d);
            float c = Vector3.Dot(m, m) - radius * radius;

            // 计算判别式
            float discriminant = b * b - 4 * a * c;

            // 无交点
            if (discriminant < 0)
                return false;

            // 计算交点参数
            float sqrtDiscriminant = MathF.Sqrt(discriminant);
            float t1 = (-b + sqrtDiscriminant) / (2 * a);
            float t2 = (-b - sqrtDiscriminant) / (2 * a);

            // 检查参数是否在[0,1]范围内
            bool hasT1 = t1 >= -tolerance && t1 <= 1 + tolerance;
            bool hasT2 = t2 >= -tolerance && t2 <= 1 + tolerance;

            if (hasT1 && hasT2)
            {
                intersectionPoints = new Vector3[2] {
                    _startPoint + t1 * d,
                    _startPoint + t2 * d
                };
                return true;
            }
            else if (hasT1)
            {
                intersectionPoints = new Vector3[1] { _startPoint + t1 * d };
                return true;
            }
            else if (hasT2)
            {
                intersectionPoints = new Vector3[1] { _startPoint + t2 * d };
                return true;
            }

            return false;
        }

        /// <summary>
        /// 将线段转换到世界坐标系下
        /// </summary>
        /// <returns>世界坐标系下的线段</returns>
        public LineSegment ToWorld()
        {
            // 将起点和终点转换到世界坐标系
            Vector3 worldStartPoint = _coordinateSystem.LocalToWorld(_startPoint);
            Vector3 worldEndPoint = _coordinateSystem.LocalToWorld(_endPoint);

            return new LineSegment(new CoordinateSystem(), worldStartPoint, worldEndPoint);
        }

        /// <summary>
        /// 将线段转换到指定坐标系下
        /// </summary>
        /// <param name="targetSystem">目标坐标系</param>
        /// <returns>目标坐标系下的线段</returns>
        public LineSegment ToCoordinateSystem(CoordinateSystem targetSystem)
        {
            // 将起点和终点转换到目标坐标系
            Vector3 targetStartPoint = targetSystem.WorldToLocal(_coordinateSystem.LocalToWorld(_startPoint));
            Vector3 targetEndPoint = targetSystem.WorldToLocal(_coordinateSystem.LocalToWorld(_endPoint));

            return new LineSegment(targetSystem, targetStartPoint, targetEndPoint);
        }

        /// <summary>
        /// 平移线段生成新实例
        /// </summary>
        /// <param name="translation">平移向量（局部坐标）</param>
        /// <returns>平移后的新线段</returns>
        public LineSegment Translate(Vector3 translation)
        {
            return new LineSegment(_coordinateSystem, _startPoint + translation, _endPoint + translation);
        }

        /// <summary>
        /// 旋转线段生成新实例
        /// </summary>
        /// <param name="rotationAxis">旋转轴（局部坐标，将被归一化）</param>
        /// <param name="rotationAngle">旋转角度（弧度）</param>
        /// <returns>旋转后的新线段</returns>
        public LineSegment Rotate(Vector3 rotationAxis, float rotationAngle)
        {
            if (rotationAxis == Vector3.Zero)
                return this;

            // 创建旋转矩阵
            Matrix4x4 rotationMatrix = Matrix4x4.CreateFromAxisAngle(rotationAxis, rotationAngle);

            // 旋转起点和终点
            Vector3 rotatedStartPoint = Vector3.Transform(_startPoint, rotationMatrix);
            Vector3 rotatedEndPoint = Vector3.Transform(_endPoint, rotationMatrix);

            return new LineSegment(_coordinateSystem, rotatedStartPoint, rotatedEndPoint);
        }

        /// <summary>
        /// 返回线段的字符串表示（默认格式）
        /// </summary>
        /// <returns>包含坐标系、起点和终点的字符串</returns>
        public override string ToString()
            => $"LineSegment [System: {_coordinateSystem.ToString("F3")}, Start: {_startPoint.ToString("F3")}, End: {_endPoint.ToString("F3")}, Length: {_length:F3}]";

        /// <summary>
        /// 返回线段的字符串表示（指定格式）
        /// </summary>
        /// <param name="format">数值格式字符串</param>
        /// <returns>格式化后的字符串</returns>
        public string ToString(string format)
            => $"LineSegment [System: {_coordinateSystem.ToString(format)}, Start: {_startPoint.ToString(format)}, End: {_endPoint.ToString(format)}, Length: {_length.ToString(format)}]";

        /// <summary>
        /// 判断两个线段是否近似相等（考虑容差）
        /// </summary>
        /// <param name="other">另一个线段</param>
        /// <param name="tolerance">容差值（默认0.0001）</param>
        /// <returns>近似相等返回true，否则false</returns>
        public bool Approximately(LineSegment other, float tolerance = 0.0001f)
        {
            // 检查坐标系是否相同
            if (!_coordinateSystem.Approximately(other._coordinateSystem, tolerance))
                return false;

            // 检查起点和终点是否近似相等（考虑方向）
            bool sameDirection =
                Vector3.Distance(_startPoint, other._startPoint) < tolerance &&
                Vector3.Distance(_endPoint, other._endPoint) < tolerance;

            bool oppositeDirection =
                Vector3.Distance(_startPoint, other._endPoint) < tolerance &&
                Vector3.Distance(_endPoint, other._startPoint) < tolerance;

            return sameDirection || oppositeDirection;
        }

        /// <summary>
        /// 创建线段的深拷贝
        /// </summary>
        /// <returns>新的线段实例</returns>
        public LineSegment Clone()
        {
            return new LineSegment(_coordinateSystem, _startPoint, _endPoint);
        }

        /// <summary>
        /// 获取线段的中点
        /// </summary>
        /// <returns>线段的中点（局部坐标）</returns>
        public Vector3 GetMidpoint()
        {
            return (_startPoint + _endPoint) / 2;
        }

        /// <summary>
        /// 获取线段上的点，通过参数t指定位置
        /// </summary>
        /// <param name="t">参数值，0表示起点，1表示终点</param>
        /// <returns>线段上的点（局部坐标）</returns>
        public Vector3 GetPointAt(float t)
        {
            return _startPoint + t * (_endPoint - _startPoint);
        }
    }
}