using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CoordinateSystem
{
    /// <summary>
    /// 三维点结构体（X/Y/Z）
    /// 系统内部基准单位：微米（μm）
    /// 支持加减乘除运算，用于坐标变换与计算
    /// </summary>
    public struct Point3D
    {
        /// <summary>X 坐标</summary>
        public double X { get; set; }

        /// <summary>Y 坐标</summary>
        public double Y { get; set; }

        /// <summary>Z 坐标</summary>
        public double Z { get; set; }

        /// <summary>
        /// 无参构造（解决 JSON 序列化 NaN 问题）
        /// 默认为 (0,0,0)
        /// </summary>
        public Point3D() : this(0, 0, 0) { }

        /// <summary>
        /// 用指定 XYZ 构造三维点
        /// </summary>
        public Point3D(double x, double y, double z) { X = x; Y = y; Z = z; }

        // 向量加法
        public static Point3D operator +(Point3D a, Point3D b) => new Point3D(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        // 向量减法
        public static Point3D operator -(Point3D a, Point3D b) => new Point3D(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        // 标量乘法
        public static Point3D operator *(Point3D p, double s) => new Point3D(p.X * s, p.Y * s, p.Z * s);

        // 标量除法
        public static Point3D operator /(Point3D p, double s) => new Point3D(p.X / s, p.Y / s, p.Z / s);

        // 分量乘法（缩放专用）
        public static Point3D operator *(Point3D p, Point3D s) => new Point3D(p.X * s.X, p.Y * s.Y, p.Z * s.Z);

        // 分量除法
        public static Point3D operator /(Point3D p, Point3D s) => new Point3D(p.X / s.X, p.Y / s.Y, p.Z / s.Z);

        /// <summary>格式化输出：X:0.0000 Y:0.0000 Z:0.0000</summary>
        public override string ToString() => $"X:{X:F4} Y:{Y:F4} Z:{Z:F4}";
    }

    /// <summary>
    /// 四元数结构体
    /// 用于表示 3D 旋转，避免欧拉角万向锁问题
    /// 支持欧拉角互转、点旋转、求逆等操作
    /// </summary>
    public struct Quaternion
    {
        /// <summary>四元数 X 分量</summary>
        public double X { get; set; }

        /// <summary>四元数 Y 分量</summary>
        public double Y { get; set; }

        /// <summary>四元数 Z 分量</summary>
        public double Z { get; set; }

        /// <summary>四元数 W 分量（实部）</summary>
        public double W { get; set; }

        /// <summary>
        /// 无参构造（解决 JSON 序列化 NaN）
        /// 默认单位四元数：(0,0,0,1)
        /// </summary>
        public Quaternion() : this(0, 0, 0, 1) { }

        /// <summary>
        /// 用 x,y,z,w 构造四元数
        /// </summary>
        public Quaternion(double x, double y, double z, double w) { X = x; Y = y; Z = z; W = w; }

        /// <summary>单位四元数（无旋转）</summary>
        public static Quaternion Identity => new Quaternion(0.0, 0.0, 0.0, 1.0);

        /// <summary>
        /// 欧拉角 ZYX 顺序 → 四元数
        /// 输入：旋转角度（度）
        /// </summary>
        public static Quaternion FromEulerZyx(double rxDeg, double ryDeg, double rzDeg)
        {
            double rx = rxDeg * CoordMath.Deg2Rad * 0.5;
            double ry = ryDeg * CoordMath.Deg2Rad * 0.5;
            double rz = rzDeg * CoordMath.Deg2Rad * 0.5;
            double cx = Math.Cos(rx), sx = Math.Sin(rx);
            double cy = Math.Cos(ry), sy = Math.Sin(ry);
            double cz = Math.Cos(rz), sz = Math.Sin(rz);
            double qw = cz * cy * cx + sz * sy * sx;
            double qx = cz * cy * sx - sz * sy * cx;
            double qy = sz * cy * sx + cz * sy * cx;
            double qz = sz * cy * cx - cz * sy * sx;
            return new Quaternion(qx, qy, qz, qw);
        }

        /// <summary>
        /// 四元数 → 欧拉角 ZYX（度）
        /// </summary>
        public (double rx, double ry, double rz) ToEulerZyx()
        {
            double x = X, y = Y, z = Z, w = W;
            double sinRy = Math.Clamp(2.0 * (w * y - z * x), -1.0, 1.0);
            double ry = Math.Asin(sinRy);
            double cy = Math.Cos(ry);
            double rx, rz;

            // 非奇异情况
            if (Math.Abs(cy) > 1e-12)
            {
                rx = Math.Atan2(2.0 * (w * x + y * z), 1.0 - 2.0 * (x * x + y * y));
                rz = Math.Atan2(2.0 * (w * z + x * y), 1.0 - 2.0 * (y * y + z * z));
            }
            // 万向锁奇异情况
            else
            {
                rx = Math.Atan2(2.0 * (y * z - w * x), 1.0 - 2.0 * (x * x + z * z));
                rz = 0.0;
            }

            return (rx * CoordMath.Rad2Deg, ry * CoordMath.Rad2Deg, rz * CoordMath.Rad2Deg);
        }

        /// <summary>
        /// 使用当前四元数旋转一个 3D 点
        /// </summary>
        public Point3D Rotate(Point3D p)
        {
            double x = X, y = Y, z = Z, w = W;
            double vx = p.X, vy = p.Y, vz = p.Z;
            double qvx = w * vx + y * vz - z * vy;
            double qvy = w * vy + z * vx - x * vz;
            double qvz = w * vz + x * vy - y * vx;
            double qvw = -x * vx - y * vy - z * vz;

            double rx = qvw * (-x) + qvx * w - qvy * z + qvz * y;
            double ry = qvw * (-y) + qvy * w - qvz * x + qvx * z;
            double rz = qvw * (-z) + qvz * w - qvx * y + qvy * x;

            return new Point3D(rx, ry, rz);
        }

        /// <summary>
        /// 求四元数的逆（用于反向旋转）
        /// </summary>
        public Quaternion Inverse() => new Quaternion(-X, -Y, -Z, W);
    }

    /// <summary>
    /// 4x4 变换矩阵
    /// 用于表示：平移 + 旋转 + 缩放
    /// 支持矩阵求逆、点乘变换、TRS 构造
    /// </summary>
    public class Matrix4x4
    {
        /// <summary>4x4 矩阵数据</summary>
        public double[,] M { get; private set; }

        /// <summary>构造并初始化为单位矩阵</summary>
        public Matrix4x4() { M = new double[4, 4]; SetIdentity(); }

        /// <summary>将矩阵设为单位矩阵</summary>
        public void SetIdentity()
        {
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++)
                    M[i, j] = i == j ? 1.0 : 0.0;
        }

        /// <summary>
        /// 从平移(Translation)、旋转(Rotation)、缩放(Scale) 构造 4x4 矩阵
        /// </summary>
        public static Matrix4x4 FromTRS(Point3D trans, Quaternion rot, Point3D scale)
        {
            var mat = new Matrix4x4();
            var (rx, ry, rz) = rot.ToEulerZyx();
            var rotMat = CoordMath.EulerZyxToMatrix(rx, ry, rz);

            // 旋转 × 缩放
            for (int i = 0; i < 3; i++)
            {
                mat.M[i, 0] = rotMat[i, 0] * scale.X;
                mat.M[i, 1] = rotMat[i, 1] * scale.Y;
                mat.M[i, 2] = rotMat[i, 2] * scale.Z;
            }

            // 平移
            mat.M[0, 3] = trans.X;
            mat.M[1, 3] = trans.Y;
            mat.M[2, 3] = trans.Z;
            mat.M[3, 3] = 1.0;

            return mat;
        }

        /// <summary>
        /// 矩阵 × 三维点（执行坐标变换）
        /// </summary>
        public Point3D MultiplyPoint(Point3D p)
        {
            double x = M[0, 0] * p.X + M[0, 1] * p.Y + M[0, 2] * p.Z + M[0, 3];
            double y = M[1, 0] * p.X + M[1, 1] * p.Y + M[1, 2] * p.Z + M[1, 3];
            double z = M[2, 0] * p.X + M[2, 1] * p.Y + M[2, 2] * p.Z + M[2, 3];
            double w = M[3, 0] * p.X + M[3, 1] * p.Y + M[3, 2] * p.Z + M[3, 3];

            // 齐次坐标归一化
            return w < 1e-12 ? new Point3D(0, 0, 0) : new Point3D(x / w, y / w, z / w);
        }

        /// <summary>
        /// 求 4x4 矩阵的逆矩阵（仅针对刚性变换/仿射变换）
        /// 若矩阵奇异则抛出异常
        /// </summary>
        public Matrix4x4 Inverse()
        {
            var inv = new Matrix4x4();
            double m00 = M[0, 0], m01 = M[0, 1], m02 = M[0, 2], m03 = M[0, 3];
            double m10 = M[1, 0], m11 = M[1, 1], m12 = M[1, 2], m13 = M[1, 3];
            double m20 = M[2, 0], m21 = M[2, 1], m22 = M[2, 2], m23 = M[2, 3];

            // 计算 3x3 子矩阵行列式
            double det = m00 * (m11 * m22 - m12 * m21) - m01 * (m10 * m22 - m12 * m20) + m02 * (m10 * m21 - m11 * m20);
            if (Math.Abs(det) < 1e-12)
                throw new InvalidOperationException("矩阵不可逆：行列式接近0");

            // 旋转部分求逆
            inv.M[0, 0] = (m11 * m22 - m12 * m21) / det;
            inv.M[0, 1] = (m02 * m21 - m01 * m22) / det;
            inv.M[0, 2] = (m01 * m12 - m02 * m11) / det;

            inv.M[1, 0] = (m12 * m20 - m10 * m22) / det;
            inv.M[1, 1] = (m00 * m22 - m02 * m20) / det;
            inv.M[1, 2] = (m02 * m10 - m00 * m12) / det;

            inv.M[2, 0] = (m10 * m21 - m11 * m20) / det;
            inv.M[2, 1] = (m01 * m20 - m00 * m21) / det;
            inv.M[2, 2] = (m00 * m11 - m01 * m10) / det;

            // 平移部分求逆
            inv.M[0, 3] = -(inv.M[0, 0] * m03 + inv.M[0, 1] * m13 + inv.M[0, 2] * m23);
            inv.M[1, 3] = -(inv.M[1, 0] * m03 + inv.M[1, 1] * m13 + inv.M[1, 2] * m23);
            inv.M[2, 3] = -(inv.M[2, 0] * m03 + inv.M[2, 1] * m13 + inv.M[2, 2] * m23);
            inv.M[3, 3] = 1.0;

            return inv;
        }
    }

    /// <summary>
    /// 坐标系数学工具类
    /// 提供：角度转换、旋转矩阵、欧拉角、单位换算等通用函数
    /// 内部基准单位：微米（μm）
    /// </summary>
    public static class CoordMath
    {
        /// <summary>弧度 → 角度</summary>
        public const double Rad2Deg = 180.0 / Math.PI;

        /// <summary>角度 → 弧度</summary>
        public const double Deg2Rad = Math.PI / 180.0;

        /// <summary>
        /// 绕任意轴旋转的旋转矩阵
        /// </summary>
        public static double[,] RotationMatrixAroundAxis(Point3D axis, double angleDeg)
        {
            double rad = angleDeg * Deg2Rad;
            double c = Math.Cos(rad), s = Math.Sin(rad), omc = 1.0 - c;
            double len = Math.Sqrt(axis.X * axis.X + axis.Y * axis.Y + axis.Z * axis.Z);
            if (len < 1e-12) len = 1.0;

            double x = axis.X / len, y = axis.Y / len, z = axis.Z / len;
            double[,] m = new double[3, 3];

            m[0, 0] = x * x * omc + c;
            m[0, 1] = x * y * omc - z * s;
            m[0, 2] = x * z * omc + y * s;

            m[1, 0] = y * x * omc + z * s;
            m[1, 1] = y * y * omc + c;
            m[1, 2] = y * z * omc - x * s;

            m[2, 0] = z * x * omc - y * s;
            m[2, 1] = z * y * omc + x * s;
            m[2, 2] = z * z * omc + c;

            return m;
        }

        /// <summary>
        /// 欧拉角 ZYX → 3x3 旋转矩阵
        /// </summary>
        public static double[,] EulerZyxToMatrix(double rxDeg, double ryDeg, double rzDeg)
        {
            double rx = rxDeg * Deg2Rad;
            double ry = ryDeg * Deg2Rad;
            double rz = rzDeg * Deg2Rad;

            double cx = Math.Cos(rx), sx = Math.Sin(rx);
            double cy = Math.Cos(ry), sy = Math.Sin(ry);
            double cz = Math.Cos(rz), sz = Math.Sin(rz);

            double[,] m = new double[3, 3];
            m[0, 0] = cy * cz;
            m[0, 1] = -cy * sz;
            m[0, 2] = sy;

            m[1, 0] = cz * sx * sy + cx * sz;
            m[1, 1] = cx * cz - sx * sy * sz;
            m[1, 2] = -cy * sx;

            m[2, 0] = -cx * cz * sy + sx * sz;
            m[2, 1] = cz * sx + cx * sy * sz;
            m[2, 2] = cy * cx;

            return m;
        }

        /// <summary>
        /// 3x3 旋转矩阵 → 欧拉角 ZYX
        /// </summary>
        public static (double rx, double ry, double rz) MatrixToEulerZyx(double[,] m)
        {
            double sy = Math.Clamp(m[0, 2], -1.0, 1.0);
            double ry = Math.Asin(sy);
            double cy = Math.Cos(ry);
            double rx, rz;

            if (Math.Abs(cy) > 1e-12)
            {
                rx = Math.Atan2(-m[1, 2], m[2, 2]);
                rz = Math.Atan2(-m[0, 1], m[0, 0]);
            }
            else
            {
                rx = Math.Atan2(m[2, 1], m[1, 1]);
                rz = 0.0;
            }

            return (rx * Rad2Deg, ry * Rad2Deg, rz * Rad2Deg);
        }

        /// <summary>
        /// 获取单位转换比例：from → to
        /// 内部基准：微米（μm）
        /// </summary>
        public static double GetUnitScale(LengthUnit from, LengthUnit to)
        {
            return ToBaseUnit(from) / ToBaseUnit(to);
        }

        /// <summary>
        /// 将任意单位转换为内部基准单位（μm）
        /// </summary>
        private static double ToBaseUnit(LengthUnit u) => u switch
        {
            LengthUnit.Nm => 0.001,      // 1 纳米 = 0.001 微米
            LengthUnit.Um => 1.0,         // 基准：微米
            LengthUnit.Mm => 1000.0,      // 1 毫米 = 1000 微米
            LengthUnit.Cm => 10000.0,     // 1 厘米 = 10000 微米
            LengthUnit.M => 1000000.0,    // 1 米 = 1000000 微米
            _ => 1.0
        };
    }
}