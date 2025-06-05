using System;
using System.Numerics;
using Entity.Geometry.Common;

namespace Entity.Geometry
{
    /// <summary>
    /// 表示三维空间中的平面，支持在指定坐标系下定义和操作
    /// 采用不可变设计，所有操作返回新的平面实例
    /// </summary>
    public sealed class Plane
    {
        /// <summary>平面所属的坐标系</summary>
        private readonly CoordinateSystem _coordinateSystem;

        /// <summary>平面上的一点（局部坐标）</summary>
        private readonly Vector3 _point;

        /// <summary>平面的法向量（局部坐标，已归一化）</summary>
        private readonly Vector3 _normal;

        /// <summary>平面的一般式系数：Ax + By + Cz + D = 0（局部坐标系下）</summary>
        private readonly float _a, _b, _c, _d;

        /// <summary>平面的局部X轴（已归一化）</summary>
        private Vector3 _xAxis;

        /// <summary>平面的局部Y轴（已归一化）</summary>
        private Vector3 _yAxis;

        /// <summary>平面的局部X轴（已归一化），只读</summary>
        public Vector3 XAxis => _xAxis;

        /// <summary>平面的局部Y轴（已归一化），只读</summary>
        public Vector3 YAxis => _yAxis;

        /// <summary>平面所属的坐标系，只读</summary>
        public CoordinateSystem CoordinateSystem => _coordinateSystem;

        /// <summary>平面上的一点（局部坐标），只读</summary>
        public Vector3 Point => _point;

        /// <summary>平面的法向量（局部坐标），只读</summary>
        public Vector3 Normal => _normal;

        /// <summary>平面的一般式系数A，只读</summary>
        public float A => _a;

        /// <summary>平面的一般式系数B，只读</summary>
        public float B => _b;

        /// <summary>平面的一般式系数C，只读</summary>
        public float C => _c;

        /// <summary>平面的一般式系数D，只读</summary>
        public float D => _d;

        /// <summary>
        /// 使用点和法向量在指定坐标系下初始化平面
        /// </summary>
        /// <param name="coordinateSystem">平面所属的坐标系</param>
        /// <param name="point">平面上的一点（局部坐标）</param>
        /// <param name="normal">平面的法向量（局部坐标，将被归一化）</param>
        /// <exception cref="ArgumentException">当法向量为零向量时抛出</exception>
        public Plane(CoordinateSystem coordinateSystem, Vector3 point, Vector3 normal)
        {
            if (normal == Vector3.Zero)
                throw new ArgumentException("法向量不能为零向量", nameof(normal));

            _coordinateSystem = coordinateSystem;
            _point = point;
            _normal = Vector3.Normalize(normal);

            // 计算一般式系数 Ax + By + Cz + D = 0
            _a = _normal.X;
            _b = _normal.Y;
            _c = _normal.Z;
            _d = -Vector3.Dot(_normal, _point);

            // 计算平面的局部X轴和Y轴（形成正交基）
            CalculateLocalAxes();
        }

        /// <summary>
        /// 使用三个不共线的点在指定坐标系下初始化平面
        /// </summary>
        /// <param name="coordinateSystem">平面所属的坐标系</param>
        /// <param name="point1">平面上的第一个点（局部坐标）</param>
        /// <param name="point2">平面上的第二个点（局部坐标）</param>
        /// <param name="point3">平面上的第三个点（局部坐标）</param>
        /// <exception cref="ArgumentException">当三点共线时抛出</exception>
        public Plane(CoordinateSystem coordinateSystem, Vector3 point1, Vector3 point2, Vector3 point3)
        {
            _coordinateSystem = coordinateSystem;
            _point = point1;

            // 计算两个向量
            Vector3 v1 = point2 - point1;
            Vector3 v2 = point3 - point1;

            // 检查是否共线
            if (Vector3.Cross(v1, v2).LengthSquared() < 0.0001f * 0.001f)
                throw new ArgumentException("三点共线，无法定义平面", nameof(point3));

            // 计算法向量并归一化
            _normal = Vector3.Normalize(Vector3.Cross(v1, v2));

            // 计算一般式系数
            _a = _normal.X;
            _b = _normal.Y;
            _c = _normal.Z;
            _d = -Vector3.Dot(_normal, _point);

            // 计算平面的局部X轴和Y轴（形成正交基）
            CalculateLocalAxes();
        }

        /// <summary>
        /// 使用一般式方程在指定坐标系下初始化平面
        /// </summary>
        /// <param name="coordinateSystem">平面所属的坐标系</param>
        /// <param name="a">一般式系数A</param>
        /// <param name="b">一般式系数B</param>
        /// <param name="c">一般式系数C</param>
        /// <param name="d">一般式系数D</param>
        /// <exception cref="ArgumentException">当法向量为零向量时抛出</exception>
        public Plane(CoordinateSystem coordinateSystem, float a, float b, float c, float d)
        {
            _coordinateSystem = coordinateSystem;
            _a = a;
            _b = b;
            _c = c;
            _d = d;

            // 计算法向量
            Vector3 normal = new Vector3(a, b, c);
            if (normal == Vector3.Zero)
                throw new ArgumentException("法向量不能为零向量", nameof(a));

            _normal = Vector3.Normalize(normal);

            // 计算平面上的一点（假设z=0，求解x和y）
            if (MathF.Abs(c) > 0.0001f)
            {
                _point = new Vector3(0, 0, -d / c);
            }
            else if (MathF.Abs(b) > 0.0001f)
            {
                _point = new Vector3(0, -d / b, 0);
            }
            else
            {
                _point = new Vector3(-d / a, 0, 0);
            }

            // 计算平面的局部X轴和Y轴（形成正交基）
            CalculateLocalAxes();
        }

        /// <summary>
        /// 计算平面的局部X轴和Y轴，确保与法向量形成正交基
        /// </summary>
        private void CalculateLocalAxes()
        {
            // 选择一个与法向量不共线的初始向量来生成X轴
            Vector3 initialX;
            if (MathF.Abs(Vector3.Dot(_normal, Vector3.UnitX)) < 0.9f)
            {
                // X轴与法向量不共线，使用UnitX
                initialX = Vector3.UnitX;
            }
            else if (MathF.Abs(Vector3.Dot(_normal, Vector3.UnitY)) < 0.9f)
            {
                // Y轴与法向量不共线，使用UnitY
                initialX = Vector3.UnitY;
            }
            else
            {
                // Z轴与法向量不共线，使用UnitZ
                initialX = Vector3.UnitZ;
            }

            // 计算X轴：初始向量在平面上的投影（去除法向量分量）
            _xAxis = Vector3.Normalize(initialX - _normal * Vector3.Dot(initialX, _normal));

            // 计算Y轴：法向量与X轴的叉积
            _yAxis = Vector3.Normalize(Vector3.Cross(_normal, _xAxis));

            // 验证并修正（处理可能的数值误差）
            _xAxis = Vector3.Normalize(_xAxis);
            _yAxis = Vector3.Normalize(Vector3.Cross(_normal, _xAxis));
        }

        /// <summary>
        /// 将平面转换到世界坐标系下
        /// </summary>
        public Plane ToWorld()
        {
            Vector3 worldPoint = _coordinateSystem.LocalToWorld(_point);
            Vector3 worldNormal = _coordinateSystem.TransformDirection(_normal);
            Vector3 worldXAxis = _coordinateSystem.TransformDirection(_xAxis);

            // 在世界坐标系下重新计算Y轴（确保正交）
            Vector3 worldYAxis = Vector3.Normalize(Vector3.Cross(worldNormal, worldXAxis));
            worldXAxis = Vector3.Normalize(Vector3.Cross(worldYAxis, worldNormal));

            return new Plane(new CoordinateSystem(), worldPoint, worldNormal)
            {
                // 注入计算好的X轴和Y轴（通过扩展方法或内部构造）
                _xAxis = worldXAxis,
                _yAxis = worldYAxis
            };
        }

        /// <summary>
        /// 将平面上的三维点转换为局部XY坐标
        /// </summary>
        public Vector2 ToLocalXY(Vector3 pointOnPlane)
        {
            if (!ContainsPoint(pointOnPlane))
                throw new ArgumentException("点不在平面上", nameof(pointOnPlane));

            Vector3 relative = pointOnPlane - _point;
            float x = Vector3.Dot(relative, _xAxis);
            float y = Vector3.Dot(relative, _yAxis);
            return new Vector2(x, y);
        }

        /// <summary>
        /// 将局部XY坐标转换为平面上的三维点
        /// </summary>
        public Vector3 FromLocalXY(Vector2 xy)
        {
            return _point + _xAxis * xy.X + _yAxis * xy.Y;
        }

        /// <summary>
        /// 检查点是否在平面上（考虑容差）
        /// </summary>
        /// <param name="point">待检查的点（局部坐标）</param>
        /// <param name="tolerance">容差值（默认0.0001）</param>
        /// <returns>点在平面上返回true，否则false</returns>
        public bool ContainsPoint(Vector3 point, float tolerance = 0.0001f)
        {
            // 计算点到平面的有符号距离
            float distance = _a * point.X + _b * point.Y + _c * point.Z + _d;
            return MathF.Abs(distance) < tolerance;
        }

        /// <summary>
        /// 计算点到平面的有符号距离（局部坐标）
        /// 距离符号由法向量方向决定
        /// </summary>
        /// <param name="point">待计算的点（局部坐标）</param>
        /// <returns>有符号距离</returns>
        public float DistanceToPoint(Vector3 point)
            => _a * point.X + _b * point.Y + _c * point.Z + _d;

        /// <summary>
        /// 计算点到平面的最短距离（无符号）
        /// </summary>
        /// <param name="point">待计算的点（局部坐标）</param>
        /// <returns>最短距离</returns>
        public float DistanceToPointUnsigned(Vector3 point)
            => MathF.Abs(DistanceToPoint(point));

        /// <summary>
        /// 将点投影到平面上（局部坐标）
        /// </summary>
        /// <param name="point">待投影的点（局部坐标）</param>
        /// <returns>投影后的点（局部坐标）</returns>
        public Vector3 ProjectPoint(Vector3 point)
        {
            float distance = DistanceToPoint(point);
            return point - _normal * distance;
        }

        /// <summary>
        /// 判断直线与平面是否相交
        /// </summary>
        /// <param name="lineStart">直线起点（局部坐标）</param>
        /// <param name="lineDirection">直线方向向量（局部坐标）</param>
        /// <param name="intersectionPoint">交点（如果相交）</param>
        /// <param name="tolerance">容差值（默认0.0001）</param>
        /// <returns>相交返回true，否则false</returns>
        public bool IntersectsLine(Vector3 lineStart, Vector3 lineDirection, out Vector3 intersectionPoint, float tolerance = 0.0001f)
        {
            intersectionPoint = Vector3.Zero;
            
            // 计算方向向量与平面法向量的点积
            float dot = Vector3.Dot(lineDirection, _normal);
            
            // 平行或重合
            if (MathF.Abs(dot) < tolerance)
                return false;
            
            // 计算交点参数t
            float t = -(Vector3.Dot(_normal, lineStart) + _d) / dot;
            intersectionPoint = lineStart + lineDirection * t;
            return true;
        }

        /// <summary>
        /// 判断线段与平面是否相交
        /// </summary>
        /// <param name="lineStart">线段起点（局部坐标）</param>
        /// <param name="lineEnd">线段终点（局部坐标）</param>
        /// <param name="intersectionPoint">交点（如果相交）</param>
        /// <param name="tolerance">容差值（默认0.0001）</param>
        /// <returns>相交返回true，否则false</returns>
        public bool IntersectsLineSegment(Vector3 lineStart, Vector3 lineEnd, out Vector3 intersectionPoint, float tolerance = 0.0001f)
        {
            intersectionPoint = Vector3.Zero;
            
            // 计算线段方向向量
            Vector3 direction = lineEnd - lineStart;
            
            // 检查直线与平面是否相交
            if (!IntersectsLine(lineStart, direction, out intersectionPoint, tolerance))
                return false;
            
            // 检查交点是否在线段范围内
            Vector3 diff = intersectionPoint - lineStart;
            float t = Vector3.Dot(diff, direction) / Vector3.Dot(direction, direction);
            return t >= -tolerance && t <= 1 + tolerance;
        }

        /// <summary>
        /// 计算两个平面的交线
        /// </summary>
        /// <param name="other">另一个平面</param>
        /// <param name="lineStart">交线起点（局部坐标）</param>
        /// <param name="lineDirection">交线方向向量（局部坐标）</param>
        /// <param name="tolerance">容差值（默认0.0001）</param>
        /// <returns>相交返回true，否则false</returns>
        public bool IntersectsPlane(Plane other, out Vector3 lineStart, out Vector3 lineDirection, float tolerance = 0.0001f)
        {
            lineStart = Vector3.Zero;
            lineDirection = Vector3.Zero;
            
            // 计算交线方向向量（两平面法向量的叉积）
            lineDirection = Vector3.Cross(_normal, other._normal);
            
            // 检查两平面是否平行或重合
            if (lineDirection.LengthSquared() < 0.001f * 0.001f || 
                MathF.Abs(Vector3.Dot(_normal, other._normal)) > 1 - tolerance)
                return false;
            
            lineDirection = Vector3.Normalize(lineDirection);
            
            // 寻找交线上的一点
            // 构造一个辅助平面来求交点
            Vector3 auxNormal = Vector3.Cross(_normal, lineDirection);
            Plane auxPlane = new Plane(_coordinateSystem, _point, auxNormal);
            
            // 求辅助平面与另一个平面的交点
            if (auxPlane.IntersectsLine(_point, lineDirection, out lineStart, tolerance))
                return true;
                
            return false;
        }

        /// <summary>
        /// 判断两个平面是否平行
        /// </summary>
        /// <param name="other">另一个平面</param>
        /// <param name="tolerance">容差值（默认0.0001）</param>
        /// <returns>平行返回true，否则false</returns>
        public bool IsParallelTo(Plane other, float tolerance = 0.0001f)
        {
            float dot = Vector3.Dot(_normal, other._normal);
            return MathF.Abs(dot) > 1 - tolerance;
        }

        /// <summary>
        /// 判断两个平面是否重合
        /// </summary>
        /// <param name="other">另一个平面</param>
        /// <param name="tolerance">容差值（默认0.0001）</param>
        /// <returns>重合返回true，否则false</returns>
        public bool IsCoplanarWith(Plane other, float tolerance = 0.0001f)
        {
            if (!IsParallelTo(other, tolerance))
                return false;
                
            // 检查点是否在另一个平面上
            return other.ContainsPoint(_point, tolerance);
        }

        /// <summary>
        /// 计算两个平面的夹角（角度制）
        /// </summary>
        /// <param name="other">另一个平面</param>
        /// <returns>夹角（0-90度）</returns>
        public float AngleWith(Plane other)
        {
            float dotProduct = MathF.Abs(Vector3.Dot(_normal, other._normal));
            dotProduct = Math.Clamp(dotProduct, 0, 1); // 防止浮点误差
            return MathF.Acos(dotProduct) * (180f / MathF.PI);
        }

        /// <summary>
        /// 将世界坐标系下的平面转换到当前坐标系下
        /// </summary>
        /// <param name="worldPlane">世界坐标系下的平面</param>
        /// <returns>当前坐标系下的平面</returns>
        public static Plane FromWorld(Plane worldPlane, CoordinateSystem targetSystem)
        {
            // 将平面上的点转换到目标坐标系的局部坐标
            Vector3 localPoint = targetSystem.WorldToLocal(worldPlane.Point);
            
            // 将法向量转换到目标坐标系的局部坐标
            Vector3 localNormal = targetSystem.InverseTransformDirection(worldPlane.Normal);
            
            // 在目标坐标系下创建新平面
            return new Plane(targetSystem, localPoint, localNormal);
        }

        /// <summary>
        /// 平移平面生成新实例
        /// </summary>
        /// <param name="translation">平移向量（局部坐标）</param>
        /// <returns>平移后的新平面</returns>
        public Plane Translate(Vector3 translation)
        {
            // 平面平移后法向量不变，只需平移平面上的点
            return new Plane(_coordinateSystem, _point + translation, _normal);
        }

        /// <summary>
        /// 绕坐标系原点旋转平面生成新实例
        /// </summary>
        /// <param name="rotationAxis">旋转轴（局部坐标，将被归一化）</param>
        /// <param name="rotationAngle">旋转角度（弧度）</param>
        /// <returns>旋转后的新平面</returns>
        /// <exception cref="ArgumentException">当旋转轴为零向量时抛出</exception>
        public Plane RotateAroundOrigin(Vector3 rotationAxis, float rotationAngle)
        {
            if (rotationAxis == Vector3.Zero)
                throw new ArgumentException("旋转轴向量不能为零向量", nameof(rotationAxis));

            // 将旋转轴转换到世界坐标
            Vector3 worldAxis = _coordinateSystem.LocalToWorldDirection(rotationAxis);
            worldAxis = Vector3.Normalize(worldAxis);

            // 创建绕世界轴的旋转矩阵
            Matrix4x4 rotationMatrix = Matrix4x4.CreateFromAxisAngle(worldAxis, rotationAngle);

            // 旋转平面上的点（相对于原点）
            Vector3 worldPoint = _coordinateSystem.LocalToWorld(_point);
            Vector3 rotatedWorldPoint = Vector3.Transform(worldPoint, rotationMatrix);
            Vector3 rotatedLocalPoint = _coordinateSystem.WorldToLocal(rotatedWorldPoint);

            // 旋转法向量
            Vector3 rotatedNormal = Vector3.TransformNormal(_normal, _coordinateSystem.RotationMatrix * rotationMatrix);
            rotatedNormal = Vector3.Normalize(rotatedNormal);

            return new Plane(_coordinateSystem, rotatedLocalPoint, rotatedNormal);
        }

        /// <summary>
        /// 绕任意点旋转平面生成新实例
        /// </summary>
        /// <param name="pivotPoint">旋转中心点（局部坐标）</param>
        /// <param name="rotationAxis">旋转轴（局部坐标，将被归一化）</param>
        /// <param name="rotationAngle">旋转角度（弧度）</param>
        /// <returns>旋转后的新平面</returns>
        public Plane RotateAroundPoint(Vector3 pivotPoint, Vector3 rotationAxis, float rotationAngle)
        {
            // 先平移使旋转中心与原点重合，旋转后再平移回原位
            Plane translatedPlane = Translate(-pivotPoint);
            Plane rotatedPlane = translatedPlane.RotateAroundOrigin(rotationAxis, rotationAngle);
            return rotatedPlane.Translate(pivotPoint);
        }

        /// <summary>
        /// 返回平面的字符串表示（默认格式）
        /// </summary>
        /// <returns>包含坐标系、点和法向量的字符串</returns>
        public override string ToString()
            => $"Plane [System: {_coordinateSystem.ToString("F3")}, Point: {_point.ToString("F3")}, Normal: {_normal.ToString("F3")}]";

        /// <summary>
        /// 返回平面的字符串表示（指定格式）
        /// </summary>
        /// <param name="format">数值格式字符串</param>
        /// <returns>格式化后的字符串</returns>
        public string ToString(string format)
            => $"Plane [System: {_coordinateSystem.ToString(format)}, Point: {_point.ToString(format)}, Normal: {_normal.ToString(format)}]";

        /// <summary>
        /// 判断两个平面是否近似相等（考虑容差）
        /// </summary>
        /// <param name="other">另一个平面</param>
        /// <param name="tolerance">容差值（默认0.0001）</param>
        /// <returns>近似相等返回true，否则false</returns>
        public bool Approximately(Plane other, float tolerance = 0.0001f)
        {
            // 检查坐标系是否相同
            if (!_coordinateSystem.Approximately(other._coordinateSystem, tolerance))
                return false;

            // 检查法向量是否近似
            if (Vector3.Dot(_normal, other._normal) < 1 - tolerance)
                return false;

            // 检查点是否在平面上
            return other.ContainsPoint(_point, tolerance);
        }

        /// <summary>
        /// 创建平面的深拷贝
        /// </summary>
        /// <returns>新的平面实例</returns>
        public Plane Clone()
        {
            return new Plane(_coordinateSystem, _point, _normal);
        }

        /// <summary>
        /// 在指定坐标系下创建XY平面（z=0）
        /// </summary>
        /// <param name="coordinateSystem">平面所属的坐标系</param>
        /// <returns>XY平面</returns>
        public static Plane CreateXYPlane(CoordinateSystem coordinateSystem)
            => new Plane(coordinateSystem, new Vector3(0, 0, 0), Vector3.UnitZ);

        /// <summary>
        /// 在指定坐标系下创建YZ平面（x=0）
        /// </summary>
        /// <param name="coordinateSystem">平面所属的坐标系</param>
        /// <returns>YZ平面</returns>
        public static Plane CreateYZPlane(CoordinateSystem coordinateSystem)
            => new Plane(coordinateSystem, new Vector3(0, 0, 0), Vector3.UnitX);

        /// <summary>
        /// 在指定坐标系下创建XZ平面（y=0）
        /// </summary>
        /// <param name="coordinateSystem">平面所属的坐标系</param>
        /// <returns>XZ平面</returns>
        public static Plane CreateXZPlane(CoordinateSystem coordinateSystem)
            => new Plane(coordinateSystem, new Vector3(0, 0, 0), Vector3.UnitY);
    }
}