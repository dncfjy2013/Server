using System;
using System.Text.Json.Serialization;

namespace CoordinateSystem
{
    /// <summary>
    /// 三维坐标系类
    /// 支持：偏移、旋转、缩放、父子层级、精度补偿、坐标变换
    /// 内部计算单位：微米（μm）
    /// </summary>
    public class CoordinateSystem3D
    {
        /// <summary>
        /// 坐标系参数变更事件
        /// </summary>
        public event EventHandler<CoordinateChangedEventArgs> Changed;

        /// <summary>
        /// 坐标系类型
        /// </summary>
        public CoordinateSystemType Type { get; set; }


        private Point3D _offset;
        private Point3D _offsetCompensation;
        private Quaternion _rotation;
        private Point3D _scale;
        private LengthUnit _unit;

        /// <summary>偏移（μm）</summary>
        public Point3D Offset => _offset;
        /// <summary>精度补偿</summary>
        public Point3D OffsetCompensation => _offsetCompensation;
        /// <summary>旋转</summary>
        public Quaternion Rotation => _rotation;
        /// <summary>缩放</summary>
        public Point3D Scale => _scale;
        /// <summary>显示单位</summary>
        public LengthUnit Unit => _unit;

        /// <summary>父坐标系类型（序列化用）</summary>
        public CoordinateSystemType? ParentType { get; set; }

        /// <summary>父坐标系</summary>
        [JsonIgnore]
        public CoordinateSystem3D Parent { get; set; }

        public CoordinateSystem3D()
        {
            _offset = new Point3D(0, 0, 0);
            _offsetCompensation = new Point3D(0, 0, 0);
            _rotation = Quaternion.Identity;
            _scale = new Point3D(1, 1, 1);
            _unit = LengthUnit.Um;
        }

        public CoordinateSystem3D(CoordinateSystemType type) : this()
        {
            Type = type;
        }

        private void Notify(string msg)
        {
            var message = $"[{Type}] {msg}";
            Changed?.Invoke(this, new CoordinateChangedEventArgs(Type, message));
        }

        public void SetOffset(double x, double y, double z)
        {
            _offset = new Point3D(x, y, z);
            Notify($"SetOffset ({x:F2},{y:F2},{z:F2})");
        }

        public void SetOffsetCompensation(double cx, double cy, double cz)
        {
            _offsetCompensation = new Point3D(cx, cy, cz);
            Notify($"SetOffsetCompensation ({cx:F4},{cy:F4},{cz:F4})");
        }

        public void Translate(double dx, double dy, double dz)
        {
            _offset = new Point3D(_offset.X + dx, _offset.Y + dy, _offset.Z + dz);
            Notify($"Translate ({dx:F2},{dy:F2},{dz:F2})");
        }

        public void SetRotationEuler(double rx, double ry, double rz)
        {
            _rotation = Quaternion.FromEulerZyx(rx, ry, rz);
            Notify($"SetRotationEuler ({rx:F1},{ry:F1},{rz:F1})");
        }

        public void RotateAroundAxis(Point3D axis, double angleDeg)
        {
            var m = CoordMath.RotationMatrixAroundAxis(axis, angleDeg);
            var e = CoordMath.MatrixToEulerZyx(m);
            SetRotationEuler(e.rx, e.ry, e.rz);
        }

        public void SetScale(double sx, double sy, double sz)
        {
            if (sx <= 0 || sy <= 0 || sz <= 0)
                throw new ArgumentOutOfRangeException("Scale Must Bigger 0");

            _scale = new Point3D(sx, sy, sz);
            Notify($"SetScale ({sx:F2},{sy:F2},{sz:F2})");
        }

        public void SetUniformScale(double s) => SetScale(s, s, s);

        public Matrix4x4 GetMatrix()
        {
            var totalOffset = _offset + _offsetCompensation;
            return Matrix4x4.FromTRS(totalOffset, _rotation, _scale);
        }

        // ====================== 坐标变换 ======================
        /// <summary>本地 → 世界</summary>
        public Point3D ConvertToWorld(Point3D local)
        {
            var p = local + _offsetCompensation;
            p *= _scale;
            p = _rotation.Rotate(p);
            p += _offset;

            return Parent?.ConvertToWorld(p) ?? p;
        }

        /// <summary>世界 → 本地</summary>
        public Point3D ConvertFromWorld(Point3D world)
        {
            var p = Parent?.ConvertFromWorld(world) ?? world;

            p -= _offset;
            p = _rotation.Inverse().Rotate(p);
            p /= _scale;
            p -= _offsetCompensation;

            return p;
        }

        public override string ToString()
        {
            return $"CoordinateSystem3D [{Type}] | " +
                   $"Offset={Offset}, " +
                   $"Compensation={OffsetCompensation}, " +
                   $"Scale={Scale}, " +
                   $"Unit={Unit}, " +
                   $"Parent={(Parent != null ? Parent.Type.ToString() : "null")}";
        }
    }
}