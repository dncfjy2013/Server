using Entity.Geometry.Common;
using System;
using System.Numerics;

namespace Entity.Geometry
{
    /// <summary>
    /// 表示三维空间中的矩形，支持在指定平面上定义和操作
    /// 采用不可变设计，所有操作返回新的矩形实例
    /// </summary>
    public sealed class Rectangle
    {
        /// <summary>矩形所在的平面</summary>
        private readonly Plane _plane;

        /// <summary>矩形中心点（局部坐标）</summary>
        private readonly Vector3 _center;

        /// <summary>矩形的宽度（X轴方向）</summary>
        private readonly float _width;

        /// <summary>矩形的高度（Y轴方向）</summary>
        private readonly float _height;

        /// <summary>矩形的四个顶点（局部坐标）</summary>
        private readonly Vector3[] _vertices;

        /// <summary>矩形所在的平面，只读</summary>
        public Plane Plane => _plane;

        /// <summary>矩形中心点（局部坐标），只读</summary>
        public Vector3 Center => _center;

        /// <summary>矩形的宽度（X轴方向），只读</summary>
        public float Width => _width;

        /// <summary>矩形的高度（Y轴方向），只读</summary>
        public float Height => _height;

        /// <summary>矩形的四个顶点（局部坐标），只读</summary>
        public ReadOnlySpan<Vector3> Vertices => _vertices;

        /// <summary>矩形的面积</summary>
        public float Area => _width * _height;

        /// <summary>矩形的周长</summary>
        public float Perimeter => 2 * (_width + _height);

        /// <summary>
        /// 在指定平面上初始化矩形
        /// </summary>
        /// <param name="plane">矩形所在的平面</param>
        /// <param name="center">矩形中心点（局部坐标）</param>
        /// <param name="width">矩形的宽度（X轴方向）</param>
        /// <param name="height">矩形的高度（Y轴方向）</param>
        /// <exception cref="ArgumentException">当宽度或高度为负数时抛出</exception>
        public Rectangle(Plane plane, Vector3 center, float width, float height)
        {
            if (width < 0)
                throw new ArgumentException("矩形的宽度不能为负数", nameof(width));

            if (height < 0)
                throw new ArgumentException("矩形的高度不能为负数", nameof(height));

            _plane = plane;
            _center = center;
            _width = width;
            _height = height;

            // 计算矩形的四个顶点
            _vertices = CalculateVertices();
        }

        /// <summary>
        /// 使用中心点、宽度、高度和法线方向在指定坐标系下初始化矩形
        /// </summary>
        /// <param name="coordinateSystem">坐标系</param>
        /// <param name="center">矩形中心点（局部坐标）</param>
        /// <param name="width">矩形的宽度（X轴方向）</param>
        /// <param name="height">矩形的高度（Y轴方向）</param>
        /// <param name="normal">法线方向（局部坐标，将被归一化）</param>
        /// <exception cref="ArgumentException">当宽度或高度为负数时抛出</exception>
        public Rectangle(CoordinateSystem coordinateSystem, Vector3 center, float width, float height, Vector3 normal)
            : this(new Plane(coordinateSystem, center, normal), center, width, height)
        {
        }

        /// <summary>
        /// 计算矩形的四个顶点
        /// </summary>
        /// <returns>包含四个顶点的数组</returns>
        private Vector3[] CalculateVertices()
        {
            float halfWidth = _width / 2;
            float halfHeight = _height / 2;

            // 获取平面的局部坐标系轴
            Vector3 xAxis = _plane.XAxis;
            Vector3 yAxis = _plane.YAxis;

            // 计算四个顶点（按顺时针顺序）
            Vector3[] vertices = new Vector3[4];
            vertices[0] = _center - xAxis * halfWidth - yAxis * halfHeight; // 左下
            vertices[1] = _center + xAxis * halfWidth - yAxis * halfHeight; // 右下
            vertices[2] = _center + xAxis * halfWidth + yAxis * halfHeight; // 右上
            vertices[3] = _center - xAxis * halfWidth + yAxis * halfHeight; // 左上

            return vertices;
        }

        /// <summary>
        /// 检查点是否在矩形上（考虑容差）
        /// </summary>
        /// <param name="point">待检查的点（局部坐标）</param>
        /// <param name="tolerance">容差值（默认0.0001）</param>
        /// <returns>点在矩形上返回true，否则false</returns>
        public bool ContainsPoint(Vector3 point, float tolerance = 0.0001f)
        {
            // 首先检查点是否在平面上
            if (!_plane.ContainsPoint(point, tolerance))
                return false;

            // 将点转换到平面的局部坐标系
            Vector3 localPoint = _plane.CoordinateSystem.WorldToLocal(point);
            Vector3 relativePoint = localPoint - _center;

            // 计算点在平面坐标系中的相对坐标
            float x = Vector3.Dot(relativePoint, _plane.XAxis);
            float y = Vector3.Dot(relativePoint, _plane.YAxis);

            // 检查是否在矩形范围内
            return MathF.Abs(x) <= _width / 2 + tolerance &&
                   MathF.Abs(y) <= _height / 2 + tolerance;
        }

        /// <summary>
        /// 计算点到矩形的最短距离
        /// </summary>
        /// <param name="point">待计算的点（局部坐标）</param>
        /// <returns>最短距离</returns>
        public float DistanceToPoint(Vector3 point)
        {
            // 计算点在平面上的投影
            Vector3 projectedPoint = _plane.ProjectPoint(point);

            // 如果投影点在矩形内，返回点到平面的距离
            if (ContainsPoint(projectedPoint))
                return _plane.DistanceToPointUnsigned(point);

            // 否则，计算点到矩形边界的最短距离
            return CalculateDistanceToBoundary(point);
        }

        /// <summary>
        /// 计算点到矩形边界的最短距离
        /// </summary>
        /// <param name="point">待计算的点（局部坐标）</param>
        /// <returns>最短距离</returns>
        private float CalculateDistanceToBoundary(Vector3 point)
        {
            // 计算点到四条边的距离，取最小值
            float minDistance = float.MaxValue;

            // 遍历四条边
            for (int i = 0; i < 4; i++)
            {
                Vector3 start = _vertices[i];
                Vector3 end = _vertices[(i + 1) % 4];

                // 计算点到线段的距离
                float distance = DistancePointToLineSegment(point, start, end);
                if (distance < minDistance)
                    minDistance = distance;
            }

            return minDistance;
        }

        /// <summary>
        /// 计算点到线段的距离
        /// </summary>
        private static float DistancePointToLineSegment(Vector3 point, Vector3 start, Vector3 end)
        {
            Vector3 ab = end - start;
            Vector3 ap = point - start;

            float abLengthSquared = Vector3.Dot(ab, ab);
            if (abLengthSquared < 0.000001f) // 线段退化为点
                return Vector3.Distance(point, start);

            float t = MathF.Max(0, MathF.Min(1, Vector3.Dot(ap, ab) / abLengthSquared));
            Vector3 projection = start + t * ab;

            return Vector3.Distance(point, projection);
        }

        /// <summary>
        /// 将点投影到矩形上
        /// 如果点在矩形内部，返回点本身；否则返回点在矩形边界上的投影
        /// </summary>
        /// <param name="point">待投影的点（局部坐标）</param>
        /// <returns>投影后的点（局部坐标）</returns>
        public Vector3 ProjectPoint(Vector3 point)
        {
            // 计算点在平面上的投影
            Vector3 projectedPoint = _plane.ProjectPoint(point);

            // 如果投影点在矩形内，直接返回
            if (ContainsPoint(projectedPoint))
                return projectedPoint;

            // 否则，找到距离最近的边并投影到该边上
            return ProjectPointToBoundary(projectedPoint);
        }

        /// <summary>
        /// 将点投影到矩形边界上
        /// </summary>
        private Vector3 ProjectPointToBoundary(Vector3 point)
        {
            // 找到距离最近的边
            float minDistance = float.MaxValue;
            Vector3 closestProjection = Vector3.Zero;

            // 遍历四条边
            for (int i = 0; i < 4; i++)
            {
                Vector3 start = _vertices[i];
                Vector3 end = _vertices[(i + 1) % 4];

                // 计算点到线段的投影
                Vector3 projection = ProjectPointToLineSegment(point, start, end);
                float distance = Vector3.Distance(point, projection);

                if (distance < minDistance)
                {
                    minDistance = distance;
                    closestProjection = projection;
                }
            }

            return closestProjection;
        }

        /// <summary>
        /// 将点投影到线段上
        /// </summary>
        private static Vector3 ProjectPointToLineSegment(Vector3 point, Vector3 start, Vector3 end)
        {
            Vector3 ab = end - start;
            Vector3 ap = point - start;

            float abLengthSquared = Vector3.Dot(ab, ab);
            if (abLengthSquared < 0.000001f) // 线段退化为点
                return start;

            float t = MathF.Max(0, MathF.Min(1, Vector3.Dot(ap, ab) / abLengthSquared));
            return start + t * ab;
        }

        /// <summary>
        /// 判断直线与矩形是否相交
        /// </summary>
        /// <param name="lineStart">直线起点（局部坐标）</param>
        /// <param name="lineDirection">直线方向向量（局部坐标）</param>
        /// <param name="intersectionPoint">交点（如果相交）</param>
        /// <param name="tolerance">容差值（默认0.0001）</param>
        /// <returns>相交返回true，否则false</returns>
        public bool IntersectsLine(Vector3 lineStart, Vector3 lineDirection, out Vector3 intersectionPoint, float tolerance = 0.0001f)
        {
            // 计算直线与平面的交点
            if (!_plane.IntersectsLine(lineStart, lineDirection, out intersectionPoint, tolerance))
                return false;

            // 检查交点是否在矩形上
            return ContainsPoint(intersectionPoint, tolerance);
        }

        /// <summary>
        /// 判断线段与矩形是否相交
        /// </summary>
        /// <param name="lineStart">线段起点（局部坐标）</param>
        /// <param name="lineEnd">线段终点（局部坐标）</param>
        /// <param name="intersectionPoint">交点（如果相交）</param>
        /// <param name="tolerance">容差值（默认0.0001）</param>
        /// <returns>相交返回true，否则false</returns>
        public bool IntersectsLineSegment(Vector3 lineStart, Vector3 lineEnd, out Vector3 intersectionPoint, float tolerance = 0.0001f)
        {
            // 计算线段与平面的交点
            if (!_plane.IntersectsLineSegment(lineStart, lineEnd, out intersectionPoint, tolerance))
                return false;

            // 检查交点是否在矩形上
            return ContainsPoint(intersectionPoint, tolerance);
        }

        /// <summary>
        /// 判断两个矩形是否相交
        /// 注意：此方法假设两个矩形在同一平面上
        /// </summary>
        /// <param name="other">另一个矩形</param>
        /// <param name="tolerance">容差值（默认0.0001）</param>
        /// <returns>相交返回true，否则false</returns>
        public bool IntersectsRectangle(Rectangle other, float tolerance = 0.0001f)
        {
            // 检查两个矩形是否在同一平面上
            if (!_plane.IsCoplanarWith(other._plane, tolerance))
                throw new InvalidOperationException("两个矩形必须在同一平面上才能判断相交");

            // 使用分离轴定理判断矩形是否相交
            return !AreRectanglesSeparated(this, other, tolerance);
        }

        /// <summary>
        /// 使用分离轴定理判断两个矩形是否分离
        /// </summary>
        private static bool AreRectanglesSeparated(Rectangle rect1, Rectangle rect2, float tolerance)
        {
            // 检查矩形1的四个边的法向量
            for (int i = 0; i < 4; i++)
            {
                Vector3 edge = rect1._vertices[(i + 1) % 4] - rect1._vertices[i];
                Vector3 axis = Vector3.Cross(edge, rect1._plane.Normal);
                axis = Vector3.Normalize(axis);

                if (AreProjectionsSeparated(rect1, rect2, axis, tolerance))
                    return true;
            }

            // 检查矩形2的四个边的法向量
            for (int i = 0; i < 4; i++)
            {
                Vector3 edge = rect2._vertices[(i + 1) % 4] - rect2._vertices[i];
                Vector3 axis = Vector3.Cross(edge, rect2._plane.Normal);
                axis = Vector3.Normalize(axis);

                if (AreProjectionsSeparated(rect1, rect2, axis, tolerance))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 检查两个矩形在给定轴上的投影是否分离
        /// </summary>
        private static bool AreProjectionsSeparated(Rectangle rect1, Rectangle rect2, Vector3 axis, float tolerance)
        {
            float min1 = float.MaxValue, max1 = float.MinValue;
            float min2 = float.MaxValue, max2 = float.MinValue;

            // 计算矩形1的顶点在轴上的投影
            foreach (var vertex in rect1._vertices)
            {
                float projection = Vector3.Dot(vertex, axis);
                min1 = MathF.Min(min1, projection);
                max1 = MathF.Max(max1, projection);
            }

            // 计算矩形2的顶点在轴上的投影
            foreach (var vertex in rect2._vertices)
            {
                float projection = Vector3.Dot(vertex, axis);
                min2 = MathF.Min(min2, projection);
                max2 = MathF.Max(max2, projection);
            }

            // 检查投影区间是否重叠
            return max1 + tolerance < min2 || max2 + tolerance < min1;
        }

        /// <summary>
        /// 将矩形转换到世界坐标系下
        /// </summary>
        /// <returns>世界坐标系下的矩形</returns>
        public Rectangle ToWorld()
        {
            // 将平面转换到世界坐标系
            Plane worldPlane = _plane.ToWorld();

            // 将中心点转换到世界坐标系
            Vector3 worldCenter = _plane.CoordinateSystem.LocalToWorld(_center);

            return new Rectangle(worldPlane, worldCenter, _width, _height);
        }

        /// <summary>
        /// 将世界坐标系下的矩形转换到当前坐标系下
        /// </summary>
        /// <param name="worldRectangle">世界坐标系下的矩形</param>
        /// <param name="targetSystem">目标坐标系</param>
        /// <returns>目标坐标系下的矩形</returns>
        public static Rectangle FromWorld(Rectangle worldRectangle, CoordinateSystem targetSystem)
        {
            // 将平面转换到目标坐标系
            Plane targetPlane = Plane.FromWorld(worldRectangle.Plane, targetSystem);

            // 将中心点转换到目标坐标系
            Vector3 targetCenter = targetSystem.WorldToLocal(worldRectangle.Center);

            return new Rectangle(targetPlane, targetCenter, worldRectangle.Width, worldRectangle.Height);
        }

        /// <summary>
        /// 平移矩形生成新实例
        /// </summary>
        /// <param name="translation">平移向量（局部坐标）</param>
        /// <returns>平移后的新矩形</returns>
        public Rectangle Translate(Vector3 translation)
        {
            // 矩形平移后平面法向量不变，只需平移中心点
            return new Rectangle(_plane, _center + translation, _width, _height);
        }

        /// <summary>
        /// 缩放矩形生成新实例
        /// </summary>
        /// <param name="scaleX">X轴方向的缩放因子</param>
        /// <param name="scaleY">Y轴方向的缩放因子</param>
        /// <returns>缩放后的新矩形</returns>
        /// <exception cref="ArgumentException">当缩放因子为负数时抛出</exception>
        public Rectangle Scale(float scaleX, float scaleY)
        {
            if (scaleX < 0)
                throw new ArgumentException("X轴缩放因子不能为负数", nameof(scaleX));

            if (scaleY < 0)
                throw new ArgumentException("Y轴缩放因子不能为负数", nameof(scaleY));

            return new Rectangle(_plane, _center, _width * scaleX, _height * scaleY);
        }

        /// <summary>
        /// 旋转矩形生成新实例
        /// </summary>
        /// <param name="rotationAxis">旋转轴（局部坐标，将被归一化）</param>
        /// <param name="rotationAngle">旋转角度（弧度）</param>
        /// <returns>旋转后的新矩形</returns>
        public Rectangle Rotate(Vector3 rotationAxis, float rotationAngle)
        {
            // 旋转平面
            Plane rotatedPlane = _plane.RotateAroundPoint(_center, rotationAxis, rotationAngle);

            return new Rectangle(rotatedPlane, _center, _width, _height);
        }

        /// <summary>
        /// 返回矩形的字符串表示（默认格式）
        /// </summary>
        /// <returns>包含平面、中心点、宽度和高度的字符串</returns>
        public override string ToString()
            => $"Rectangle [Plane: {_plane.ToString("F3")}, Center: {_center.ToString("F3")}, Width: {_width:F3}, Height: {_height:F3}]";

        /// <summary>
        /// 返回矩形的字符串表示（指定格式）
        /// </summary>
        /// <param name="format">数值格式字符串</param>
        /// <returns>格式化后的字符串</returns>
        public string ToString(string format)
            => $"Rectangle [Plane: {_plane.ToString(format)}, Center: {_center.ToString(format)}, Width: {_width.ToString(format)}, Height: {_height.ToString(format)}]";

        /// <summary>
        /// 判断两个矩形是否近似相等（考虑容差）
        /// </summary>
        /// <param name="other">另一个矩形</param>
        /// <param name="tolerance">容差值（默认0.0001）</param>
        /// <returns>近似相等返回true，否则false</returns>
        public bool Approximately(Rectangle other, float tolerance = 0.0001f)
        {
            // 检查平面是否相同
            if (!_plane.Approximately(other._plane, tolerance))
                return false;

            // 检查中心点是否近似
            if (Vector3.Distance(_center, other._center) > tolerance)
                return false;

            // 检查尺寸是否近似
            return MathF.Abs(_width - other._width) < tolerance &&
                   MathF.Abs(_height - other._height) < tolerance;
        }

        /// <summary>
        /// 创建矩形的深拷贝
        /// </summary>
        /// <returns>新的矩形实例</returns>
        public Rectangle Clone()
        {
            return new Rectangle(_plane, _center, _width, _height);
        }

        /// <summary>
        /// 在指定坐标系下创建XY平面上的矩形
        /// </summary>
        /// <param name="coordinateSystem">坐标系</param>
        /// <param name="center">矩形中心点（局部坐标）</param>
        /// <param name="width">矩形的宽度（X轴方向）</param>
        /// <param name="height">矩形的高度（Y轴方向）</param>
        /// <returns>XY平面上的矩形</returns>
        public static Rectangle CreateXYRectangle(CoordinateSystem coordinateSystem, Vector3 center, float width, float height)
        {
            Plane xyPlane = Plane.CreateXYPlane(coordinateSystem);
            return new Rectangle(xyPlane, center, width, height);
        }

        /// <summary>
        /// 在指定坐标系下创建YZ平面上的矩形
        /// </summary>
        /// <param name="coordinateSystem">坐标系</param>
        /// <param name="center">矩形中心点（局部坐标）</param>
        /// <param name="width">矩形的宽度（Y轴方向）</param>
        /// <param name="height">矩形的高度（Z轴方向）</param>
        /// <returns>YZ平面上的矩形</returns>
        public static Rectangle CreateYZRectangle(CoordinateSystem coordinateSystem, Vector3 center, float width, float height)
        {
            Plane yzPlane = Plane.CreateYZPlane(coordinateSystem);
            return new Rectangle(yzPlane, center, width, height);
        }

        /// <summary>
        /// 在指定坐标系下创建XZ平面上的矩形
        /// </summary>
        /// <param name="coordinateSystem">坐标系</param>
        /// <param name="center">矩形中心点（局部坐标）</param>
        /// <param name="width">矩形的宽度（X轴方向）</param>
        /// <param name="height">矩形的高度（Z轴方向）</param>
        /// <returns>XZ平面上的矩形</returns>
        public static Rectangle CreateXZRectangle(CoordinateSystem coordinateSystem, Vector3 center, float width, float height)
        {
            Plane xzPlane = Plane.CreateXZPlane(coordinateSystem);
            return new Rectangle(xzPlane, center, width, height);
        }
    }
}