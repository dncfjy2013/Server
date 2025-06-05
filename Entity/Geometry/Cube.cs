using Entity.Geometry.Common;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Entity.Geometry
{
    /// <summary>
    /// 表示三维空间中的立方体，支持在指定坐标系下定义和操作
    /// 采用不可变设计，所有操作返回新的立方体实例
    /// </summary>
    public sealed class Cube
    {
        /// <summary>立方体所属的坐标系</summary>
        private readonly CoordinateSystem _coordinateSystem;

        /// <summary>立方体的中心（局部坐标）</summary>
        private readonly Vector3 _center;

        /// <summary>立方体的边长</summary>
        private readonly float _edgeLength;

        /// <summary>立方体的八个顶点（局部坐标）</summary>
        private readonly Vector3[] _vertices;

        /// <summary>立方体的六个面的平面（局部坐标）</summary>
        private readonly Plane[] _faces;

        /// <summary>立方体所属的坐标系，只读</summary>
        public CoordinateSystem CoordinateSystem => _coordinateSystem;

        /// <summary>立方体的中心（局部坐标），只读</summary>
        public Vector3 Center => _center;

        /// <summary>立方体的边长，只读</summary>
        public float EdgeLength => _edgeLength;

        /// <summary>立方体的体积</summary>
        public float Volume => _edgeLength * _edgeLength * _edgeLength;

        /// <summary>立方体的表面积</summary>
        public float SurfaceArea => 6 * _edgeLength * _edgeLength;

        /// <summary>
        /// 在指定坐标系下初始化立方体
        /// </summary>
        /// <param name="coordinateSystem">立方体所属的坐标系</param>
        /// <param name="center">立方体的中心（局部坐标）</param>
        /// <param name="edgeLength">立方体的边长</param>
        /// <exception cref="ArgumentException">当边长为负数时抛出</exception>
        public Cube(CoordinateSystem coordinateSystem, Vector3 center, float edgeLength)
        {
            if (edgeLength < 0)
                throw new ArgumentException("立方体边长不能为负数", nameof(edgeLength));

            _coordinateSystem = coordinateSystem;
            _center = center;
            _edgeLength = edgeLength;
            _vertices = CalculateVertices();
            _faces = CalculateFaces();
        }

        /// <summary>
        /// 计算立方体的八个顶点
        /// </summary>
        private Vector3[] CalculateVertices()
        {
            float halfLength = _edgeLength / 2;
            Vector3[] vertices = new Vector3[8];

            // 基于坐标系的三个轴方向定义顶点
            Vector3 xAxis = _coordinateSystem.XAxis * halfLength;
            Vector3 yAxis = _coordinateSystem.YAxis * halfLength;
            Vector3 zAxis = _coordinateSystem.ZAxis * halfLength;

            // 顶点顺序：左下后、右下后、右上后、左上后、左下前、右下前、右上前、左上前
            vertices[0] = _center - xAxis - yAxis - zAxis;
            vertices[1] = _center + xAxis - yAxis - zAxis;
            vertices[2] = _center + xAxis + yAxis - zAxis;
            vertices[3] = _center - xAxis + yAxis - zAxis;
            vertices[4] = _center - xAxis - yAxis + zAxis;
            vertices[5] = _center + xAxis - yAxis + zAxis;
            vertices[6] = _center + xAxis + yAxis + zAxis;
            vertices[7] = _center - xAxis + yAxis + zAxis;

            return vertices;
        }

        /// <summary>
        /// 计算立方体的六个面的平面
        /// </summary>
        private Plane[] CalculateFaces()
        {
            Plane[] faces = new Plane[6];
            float halfLength = _edgeLength / 2;

            // 基于坐标系的三个轴方向定义面
            Vector3 xAxis = _coordinateSystem.XAxis;
            Vector3 yAxis = _coordinateSystem.YAxis;
            Vector3 zAxis = _coordinateSystem.ZAxis;

            // 右面
            faces[0] = new Plane(_coordinateSystem, _center + xAxis * halfLength, xAxis);
            // 左面
            faces[1] = new Plane(_coordinateSystem, _center - xAxis * halfLength, -xAxis);
            // 上面
            faces[2] = new Plane(_coordinateSystem, _center + yAxis * halfLength, yAxis);
            // 下面
            faces[3] = new Plane(_coordinateSystem, _center - yAxis * halfLength, -yAxis);
            // 前面
            faces[4] = new Plane(_coordinateSystem, _center + zAxis * halfLength, zAxis);
            // 后面
            faces[5] = new Plane(_coordinateSystem, _center - zAxis * halfLength, -zAxis);

            return faces;
        }

        /// <summary>
        /// 获取立方体的顶点（局部坐标）
        /// </summary>
        /// <param name="index">顶点索引（0-7）</param>
        /// <returns>顶点坐标</returns>
        public Vector3 GetVertex(int index)
        {
            if (index < 0 || index >= 8)
                throw new IndexOutOfRangeException("顶点索引必须在0-7之间");

            return _vertices[index];
        }

        /// <summary>
        /// 获取立方体的面平面（局部坐标）
        /// </summary>
        /// <param name="index">面索引（0-5）：右、左、上、下、前、后</param>
        /// <returns>面平面</returns>
        public Plane GetFace(int index)
        {
            if (index < 0 || index >= 6)
                throw new IndexOutOfRangeException("面索引必须在0-5之间");

            return _faces[index];
        }

        /// <summary>
        /// 检查点是否在立方体内（包括表面）
        /// </summary>
        /// <param name="point">待检查的点（局部坐标）</param>
        /// <param name="tolerance">容差值（默认0.0001）</param>
        /// <returns>点在立方体内返回true，否则false</returns>
        public bool ContainsPoint(Vector3 point, float tolerance = 0.0001f)
        {
            // 计算点相对于中心的位置
            Vector3 relativePoint = point - _center;

            // 获取坐标系的三个轴方向
            Vector3 xAxis = _coordinateSystem.XAxis;
            Vector3 yAxis = _coordinateSystem.YAxis;
            Vector3 zAxis = _coordinateSystem.ZAxis;

            // 计算点在三个轴上的投影长度
            float halfLength = _edgeLength / 2;
            float xProjection = MathF.Abs(Vector3.Dot(relativePoint, xAxis));
            float yProjection = MathF.Abs(Vector3.Dot(relativePoint, yAxis));
            float zProjection = MathF.Abs(Vector3.Dot(relativePoint, zAxis));

            // 检查是否在立方体内
            return xProjection <= halfLength + tolerance &&
                   yProjection <= halfLength + tolerance &&
                   zProjection <= halfLength + tolerance;
        }

        /// <summary>
        /// 计算点到立方体的最短距离
        /// </summary>
        /// <param name="point">待计算的点（局部坐标）</param>
        /// <returns>最短距离</returns>
        public float DistanceToPoint(Vector3 point)
        {
            // 计算点相对于中心的位置
            Vector3 relativePoint = point - _center;

            // 获取坐标系的三个轴方向
            Vector3 xAxis = _coordinateSystem.XAxis;
            Vector3 yAxis = _coordinateSystem.YAxis;
            Vector3 zAxis = _coordinateSystem.ZAxis;

            // 计算点在三个轴上的投影长度
            float halfLength = _edgeLength / 2;
            float xProjection = Vector3.Dot(relativePoint, xAxis);
            float yProjection = Vector3.Dot(relativePoint, yAxis);
            float zProjection = Vector3.Dot(relativePoint, zAxis);

            // 计算超出边界的距离
            float dx = MathF.Max(0, MathF.Abs(xProjection) - halfLength);
            float dy = MathF.Max(0, MathF.Abs(yProjection) - halfLength);
            float dz = MathF.Max(0, MathF.Abs(zProjection) - halfLength);

            // 返回欧几里得距离
            return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        /// <summary>
        /// 判断线段与立方体是否相交
        /// </summary>
        /// <param name="line">线段</param>
        /// <param name="intersectionPoints">交点数组（最多两个）</param>
        /// <param name="tolerance">容差值（默认0.0001）</param>
        /// <returns>相交返回true，否则false</returns>
        public bool IntersectsLineSegment(LineSegment line, out Vector3[] intersectionPoints, float tolerance = 0.0001f)
        {
            intersectionPoints = Array.Empty<Vector3>();

            // 将线段转换到立方体的坐标系
            LineSegment lineInCubeSystem = line.ToCoordinateSystem(_coordinateSystem);

            Vector3 start = lineInCubeSystem.StartPoint;
            Vector3 end = lineInCubeSystem.EndPoint;

            // 使用分离轴定理(SAT)检测线段与立方体的相交
            // 线段方向
            Vector3 lineDirection = end - start;
            float lineLength = lineDirection.Length();

            if (lineLength < tolerance)
            {
                // 线段退化为点
                if (ContainsPoint(start, tolerance))
                {
                    intersectionPoints = new[] { start };
                    return true;
                }
                return false;
            }

            lineDirection /= lineLength;

            // 线段的中心点和半长度向量
            Vector3 lineCenter = (start + end) / 2;
            Vector3 lineHalfVector = lineDirection * (lineLength / 2);

            // 立方体的轴
            Vector3[] cubeAxes = new Vector3[]
            {
                _coordinateSystem.XAxis,
                _coordinateSystem.YAxis,
                _coordinateSystem.ZAxis
            };

            // 线段的轴（只有一个方向）
            Vector3[] lineAxes = new Vector3[] { lineDirection };

            // 计算所有可能的分离轴
            List<Vector3> axes = new List<Vector3>();
            axes.AddRange(cubeAxes);
            axes.AddRange(lineAxes);

            // 添加立方体轴与线段轴的叉积
            foreach (var cubeAxis in cubeAxes)
            {
                foreach (var lineAxis in lineAxes)
                {
                    Vector3 cross = Vector3.Cross(cubeAxis, lineAxis);
                    if (cross.LengthSquared() > tolerance)
                    {
                        axes.Add(Vector3.Normalize(cross));
                    }
                }
            }

            // 对每个分离轴进行投影测试
            foreach (var axis in axes)
            {
                // 计算立方体在轴上的投影范围
                float cubeMin = float.MaxValue;
                float cubeMax = float.MinValue;
                foreach (var vertex in _vertices)
                {
                    float projection = Vector3.Dot(vertex, axis);
                    cubeMin = MathF.Min(cubeMin, projection);
                    cubeMax = MathF.Max(cubeMax, projection);
                }

                // 计算线段在轴上的投影范围
                float lineCenterProj = Vector3.Dot(lineCenter, axis);
                float lineRadius = MathF.Abs(Vector3.Dot(lineHalfVector, axis));
                float lineMin = lineCenterProj - lineRadius;
                float lineMax = lineCenterProj + lineRadius;

                // 检查投影是否重叠
                if (cubeMax < lineMin - tolerance || lineMax < cubeMin - tolerance)
                {
                    // 找到分离轴，没有相交
                    return false;
                }
            }

            // 没有找到分离轴，有相交
            // 计算实际的交点
            List<Vector3> intersections = new List<Vector3>();

            // 检查线段的端点是否在立方体内
            bool startInside = ContainsPoint(start, tolerance);
            bool endInside = ContainsPoint(end, tolerance);

            if (startInside)
                intersections.Add(start);

            if (endInside)
                intersections.Add(end);

            // 如果两个端点都在内部，直接返回这两个点
            if (intersections.Count == 2)
            {
                intersectionPoints = intersections.ToArray();
                return true;
            }

            // 计算线段与六个面的交点
            for (int i = 0; i < 6; i++)
            {
                Plane face = _faces[i];
                if (lineInCubeSystem.IntersectsPlane(face, out Vector3 intersection, tolerance))
                {
                    // 检查交点是否在面的边界内
                    if (IsPointOnFace(intersection, i, tolerance))
                    {
                        // 检查是否已经包含这个交点
                        bool isDuplicate = false;
                        foreach (var point in intersections)
                        {
                            if (Vector3.Distance(point, intersection) < tolerance)
                            {
                                isDuplicate = true;
                                break;
                            }
                        }

                        if (!isDuplicate)
                            intersections.Add(intersection);
                    }
                }
            }

            if (intersections.Count > 0)
            {
                intersectionPoints = intersections.ToArray();
                return true;
            }

            return false;
        }

        /// <summary>
        /// 判断点是否在指定面上
        /// </summary>
        private bool IsPointOnFace(Vector3 point, int faceIndex, float tolerance = 0.0001f)
        {
            float halfLength = _edgeLength / 2;
            Vector3 relativePoint = point - _center;

            // 检查点是否在面上
            switch (faceIndex)
            {
                case 0: // 右面
                    return MathF.Abs(Vector3.Dot(relativePoint, _coordinateSystem.XAxis) - halfLength) < tolerance &&
                           MathF.Abs(Vector3.Dot(relativePoint, _coordinateSystem.YAxis)) <= halfLength + tolerance &&
                           MathF.Abs(Vector3.Dot(relativePoint, _coordinateSystem.ZAxis)) <= halfLength + tolerance;
                case 1: // 左面
                    return MathF.Abs(Vector3.Dot(relativePoint, _coordinateSystem.XAxis) + halfLength) < tolerance &&
                           MathF.Abs(Vector3.Dot(relativePoint, _coordinateSystem.YAxis)) <= halfLength + tolerance &&
                           MathF.Abs(Vector3.Dot(relativePoint, _coordinateSystem.ZAxis)) <= halfLength + tolerance;
                case 2: // 上面
                    return MathF.Abs(Vector3.Dot(relativePoint, _coordinateSystem.YAxis) - halfLength) < tolerance &&
                           MathF.Abs(Vector3.Dot(relativePoint, _coordinateSystem.XAxis)) <= halfLength + tolerance &&
                           MathF.Abs(Vector3.Dot(relativePoint, _coordinateSystem.ZAxis)) <= halfLength + tolerance;
                case 3: // 下面
                    return MathF.Abs(Vector3.Dot(relativePoint, _coordinateSystem.YAxis) + halfLength) < tolerance &&
                           MathF.Abs(Vector3.Dot(relativePoint, _coordinateSystem.XAxis)) <= halfLength + tolerance &&
                           MathF.Abs(Vector3.Dot(relativePoint, _coordinateSystem.ZAxis)) <= halfLength + tolerance;
                case 4: // 前面
                    return MathF.Abs(Vector3.Dot(relativePoint, _coordinateSystem.ZAxis) - halfLength) < tolerance &&
                           MathF.Abs(Vector3.Dot(relativePoint, _coordinateSystem.XAxis)) <= halfLength + tolerance &&
                           MathF.Abs(Vector3.Dot(relativePoint, _coordinateSystem.YAxis)) <= halfLength + tolerance;
                case 5: // 后面
                    return MathF.Abs(Vector3.Dot(relativePoint, _coordinateSystem.ZAxis) + halfLength) < tolerance &&
                           MathF.Abs(Vector3.Dot(relativePoint, _coordinateSystem.XAxis)) <= halfLength + tolerance &&
                           MathF.Abs(Vector3.Dot(relativePoint, _coordinateSystem.YAxis)) <= halfLength + tolerance;
                default:
                    throw new ArgumentException("面索引必须在0-5之间", nameof(faceIndex));
            }
        }

        /// <summary>
        /// 判断立方体与球是否相交
        /// </summary>
        /// <param name="sphere">球</param>
        /// <param name="tolerance">容差值（默认0.0001）</param>
        /// <returns>相交返回true，否则false</returns>
        public bool IntersectsSphere(Sphere sphere, float tolerance = 0.0001f)
        {
            // 将球转换到立方体的坐标系
            Sphere sphereInCubeSystem = sphere.ToCoordinateSystem(_coordinateSystem);

            Vector3 sphereCenter = sphereInCubeSystem.Center;
            float sphereRadius = sphereInCubeSystem.Radius;

            // 计算球心到立方体的最短距离
            float distance = DistanceToPoint(sphereCenter);

            // 如果距离小于等于球的半径，则相交
            return distance <= sphereRadius + tolerance;
        }

        /// <summary>
        /// 判断立方体与圆柱是否相交
        /// </summary>
        /// <param name="cylinder">圆柱</param>
        /// <param name="tolerance">容差值（默认0.0001）</param>
        /// <returns>相交返回true，否则false</returns>
        public bool IntersectsCylinder(Cylinder cylinder, float tolerance = 0.0001f)
        {
            // 使用分离轴定理检测立方体与圆柱的相交
            // 将圆柱转换到立方体的坐标系
            Cylinder cylinderInCubeSystem = cylinder.ToCoordinateSystem(_coordinateSystem);

            // 立方体的轴
            Vector3[] cubeAxes = new Vector3[]
            {
                _coordinateSystem.XAxis,
                _coordinateSystem.YAxis,
                _coordinateSystem.ZAxis
            };

            // 圆柱的轴
            Vector3 cylinderAxis = cylinderInCubeSystem.Axis;

            // 圆柱底面的两个正交轴
            Vector3 cylinderXAxis, cylinderYAxis;
            CreatePerpendicularVectors(cylinderAxis, out cylinderXAxis, out cylinderYAxis);

            // 圆柱的轴和底面的两个正交轴
            Vector3[] cylinderAxes = new Vector3[]
            {
                cylinderAxis,
                cylinderXAxis,
                cylinderYAxis
            };

            // 计算所有可能的分离轴
            List<Vector3> axes = new List<Vector3>();

            // 立方体的三个轴
            axes.AddRange(cubeAxes);

            // 圆柱的三个轴
            axes.AddRange(cylinderAxes);

            // 立方体轴与圆柱轴的叉积
            foreach (var cubeAxis in cubeAxes)
            {
                foreach (var cylAxis in cylinderAxes)
                {
                    Vector3 cross = Vector3.Cross(cubeAxis, cylAxis);
                    if (cross.LengthSquared() > tolerance)
                    {
                        axes.Add(Vector3.Normalize(cross));
                    }
                }
            }

            // 对每个分离轴进行投影测试
            foreach (var axis in axes)
            {
                // 计算立方体在轴上的投影范围
                float cubeMin = float.MaxValue;
                float cubeMax = float.MinValue;
                foreach (var vertex in _vertices)
                {
                    float projection = Vector3.Dot(vertex, axis);
                    cubeMin = MathF.Min(cubeMin, projection);
                    cubeMax = MathF.Max(cubeMax, projection);
                }

                // 计算圆柱在轴上的投影范围
                float cylinderMin, cylinderMax;
                CalculateCylinderProjection(cylinderInCubeSystem, axis, out cylinderMin, out cylinderMax);

                // 检查投影是否重叠
                if (cubeMax < cylinderMin - tolerance || cylinderMax < cubeMin - tolerance)
                {
                    // 找到分离轴，没有相交
                    return false;
                }
            }

            // 没有找到分离轴，有相交
            return true;
        }

        /// <summary>
        /// 计算圆柱在指定轴上的投影范围
        /// </summary>
        private void CalculateCylinderProjection(Cylinder cylinder, Vector3 axis, out float min, out float max)
        {
            // 计算底面和顶面中心的投影
            float baseCenterProj = Vector3.Dot(cylinder.BaseCenter, axis);
            float topCenterProj = Vector3.Dot(cylinder.TopCenter, axis);

            // 计算圆柱半径在轴上的投影贡献
            Vector3 axisOnPlane = axis - Vector3.Dot(axis, cylinder.Axis) * cylinder.Axis;
            float radiusProj = cylinder.Radius * axisOnPlane.Length();

            // 计算圆柱的最小和最大投影
            min = MathF.Min(baseCenterProj, topCenterProj) - radiusProj;
            max = MathF.Max(baseCenterProj, topCenterProj) + radiusProj;
        }

        /// <summary>
        /// 创建与给定向量垂直的两个正交向量
        /// </summary>
        private void CreatePerpendicularVectors(Vector3 vector, out Vector3 perpendicular1, out Vector3 perpendicular2)
        {
            // 找到一个不平行于vector的向量
            Vector3 temp = (MathF.Abs(vector.X) < 0.9f) ? Vector3.UnitX : Vector3.UnitY;

            // 计算第一个垂直向量
            perpendicular1 = Vector3.Normalize(Vector3.Cross(vector, temp));

            // 计算第二个垂直向量
            perpendicular2 = Vector3.Normalize(Vector3.Cross(vector, perpendicular1));
        }

        /// <summary>
        /// 将立方体转换到世界坐标系下
        /// </summary>
        /// <returns>世界坐标系下的立方体</returns>
        public Cube ToWorld()
        {
            // 将中心转换到世界坐标系
            Vector3 worldCenter = _coordinateSystem.LocalToWorld(_center);

            // 使用世界坐标系
            return new Cube(new CoordinateSystem(), worldCenter, _edgeLength);
        }

        /// <summary>
        /// 将立方体转换到指定坐标系下
        /// </summary>
        /// <param name="targetSystem">目标坐标系</param>
        /// <returns>目标坐标系下的立方体</returns>
        public Cube ToCoordinateSystem(CoordinateSystem targetSystem)
        {
            // 将中心转换到目标坐标系
            Vector3 targetCenter = targetSystem.WorldToLocal(_coordinateSystem.LocalToWorld(_center));

            return new Cube(targetSystem, targetCenter, _edgeLength);
        }

        /// <summary>
        /// 平移立方体生成新实例
        /// </summary>
        /// <param name="translation">平移向量（局部坐标）</param>
        /// <returns>平移后的新立方体</returns>
        public Cube Translate(Vector3 translation)
        {
            return new Cube(_coordinateSystem, _center + translation, _edgeLength);
        }

        /// <summary>
        /// 缩放立方体生成新实例
        /// </summary>
        /// <param name="scale">缩放因子</param>
        /// <returns>缩放后的新立方体</returns>
        /// <exception cref="ArgumentException">当缩放因子为负数时抛出</exception>
        public Cube Scale(float scale)
        {
            if (scale < 0)
                throw new ArgumentException("缩放因子不能为负数", nameof(scale));

            return new Cube(_coordinateSystem, _center, _edgeLength * scale);
        }

        /// <summary>
        /// 旋转立方体生成新实例
        /// </summary>
        /// <param name="rotationAxis">旋转轴（局部坐标，将被归一化）</param>
        /// <param name="rotationAngle">旋转角度（弧度）</param>
        /// <returns>旋转后的新立方体</returns>
        public Cube Rotate(Vector3 rotationAxis, float rotationAngle)
        {
            if (rotationAxis == Vector3.Zero)
                return this;

            // 创建旋转矩阵
            Matrix4x4 rotationMatrix = Matrix4x4.CreateFromAxisAngle(rotationAxis, rotationAngle);

            // 旋转中心
            Vector3 rotatedCenter = Vector3.Transform(_center, rotationMatrix);

            // 创建新的坐标系（保持原点不变，只旋转坐标轴）
            CoordinateSystem rotatedSystem = new CoordinateSystem(
                _coordinateSystem.Origin,
                Vector3.TransformNormal(_coordinateSystem.XAxis, rotationMatrix),
                Vector3.TransformNormal(_coordinateSystem.YAxis, rotationMatrix),
                Vector3.TransformNormal(_coordinateSystem.ZAxis, rotationMatrix)
            );

            return new Cube(rotatedSystem, rotatedCenter, _edgeLength);
        }

        /// <summary>
        /// 返回立方体的字符串表示（默认格式）
        /// </summary>
        /// <returns>包含坐标系、中心和边长的字符串</returns>
        public override string ToString()
            => $"Cube [System: {_coordinateSystem.ToString("F3")}, Center: {_center.ToString("F3")}, EdgeLength: {_edgeLength:F3}]";

        /// <summary>
        /// 返回立方体的字符串表示（指定格式）
        /// </summary>
        /// <param name="format">数值格式字符串</param>
        /// <returns>格式化后的字符串</returns>
        public string ToString(string format)
            => $"Cube [System: {_coordinateSystem.ToString(format)}, Center: {_center.ToString(format)}, EdgeLength: {_edgeLength.ToString(format)}]";

        /// <summary>
        /// 判断两个立方体是否近似相等（考虑容差）
        /// </summary>
        /// <param name="other">另一个立方体</param>
        /// <param name="tolerance">容差值（默认0.0001）</param>
        /// <returns>近似相等返回true，否则false</returns>
        public bool Approximately(Cube other, float tolerance = 0.0001f)
        {
            // 检查坐标系是否相同
            if (!_coordinateSystem.Approximately(other._coordinateSystem, tolerance))
                return false;

            // 检查中心是否近似相等
            if (Vector3.Distance(_center, other._center) > tolerance)
                return false;

            // 检查边长是否近似相等
            return MathF.Abs(_edgeLength - other._edgeLength) <= tolerance;
        }

        /// <summary>
        /// 创建立方体的深拷贝
        /// </summary>
        /// <returns>新的立方体实例</returns>
        public Cube Clone()
        {
            return new Cube(_coordinateSystem, _center, _edgeLength);
        }

        /// <summary>
        /// 获取立方体的所有棱边
        /// </summary>
        /// <returns>棱边线段数组</returns>
        public LineSegment[] GetEdges()
        {
            LineSegment[] edges = new LineSegment[12];

            // 底面四条边
            edges[0] = new LineSegment(_coordinateSystem, _vertices[0], _vertices[1]);
            edges[1] = new LineSegment(_coordinateSystem, _vertices[1], _vertices[2]);
            edges[2] = new LineSegment(_coordinateSystem, _vertices[2], _vertices[3]);
            edges[3] = new LineSegment(_coordinateSystem, _vertices[3], _vertices[0]);

            // 顶面四条边
            edges[4] = new LineSegment(_coordinateSystem, _vertices[4], _vertices[5]);
            edges[5] = new LineSegment(_coordinateSystem, _vertices[5], _vertices[6]);
            edges[6] = new LineSegment(_coordinateSystem, _vertices[6], _vertices[7]);
            edges[7] = new LineSegment(_coordinateSystem, _vertices[7], _vertices[4]);

            // 垂直四条边
            edges[8] = new LineSegment(_coordinateSystem, _vertices[0], _vertices[4]);
            edges[9] = new LineSegment(_coordinateSystem, _vertices[1], _vertices[5]);
            edges[10] = new LineSegment(_coordinateSystem, _vertices[2], _vertices[6]);
            edges[11] = new LineSegment(_coordinateSystem, _vertices[3], _vertices[7]);

            return edges;
        }
    }
}