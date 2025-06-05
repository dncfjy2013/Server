using MathNet.Numerics.LinearAlgebra;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Entity.Geometry.Common
{
    public class Coordinate2DAngel()
    {
        public static double CalculateInitialAngle(List<Vector2> originalPoints, List<Vector2> measuredPoints)
        {
            if (originalPoints == null || measuredPoints == null)
                throw new ArgumentNullException("点集不能为null");

            if (originalPoints.Count != measuredPoints.Count)
                throw new ArgumentException("点集数量必须相同");

            if (originalPoints.Count < 2)
                throw new ArgumentException("至少需要两个点来计算旋转角度");

            var originalCentroid = CalculateCentroid(originalPoints);
            var measuredCentroid = CalculateCentroid(measuredPoints);

            // 计算中心化后的点集并构建协方差矩阵
            double sumXX = 0, sumXY = 0, sumYX = 0, sumYY = 0;

            for (int i = 0; i < originalPoints.Count; i++)
            {
                var o = originalPoints[i] - originalCentroid;
                var m = measuredPoints[i] - measuredCentroid;

                sumXX += o.X * m.X;
                sumXY += o.X * m.Y;
                sumYX += o.Y * m.X;
                sumYY += o.Y * m.Y;
            }

            // 直接从协方差矩阵计算旋转角度，无需显式构建矩阵
            double trace = sumXX + sumYY;
            double delta = sumXY - sumYX;

            // 使用四元数方法直接计算旋转角度（更高效）
            double theta = Math.Atan2(delta, trace);

            // 将弧度转换为角度
            return theta * (180.0 / Math.PI);
        }

        /// <summary>
        /// 使用Kahan求和算法计算质心，提高精度
        /// </summary>
        private static Vector2 CalculateCentroid(List<Vector2> points)
        {
            double sumX = 0, sumY = 0;
            double cX = 0, cY = 0; // Kahan求和补偿项

            foreach (var p in points)
            {
                double yX = p.X - cX;
                double tX = sumX + yX;
                cX = tX - sumX - yX;
                sumX = tX;

                double yY = p.Y - cY;
                double tY = sumY + yY;
                cY = tY - sumY - yY;
                sumY = tY;
            }

            int count = points.Count;
            return count > 0 ? new Vector2((float)(sumX / count), (float)(sumY / count)) : Vector2.Zero;
        }
    }
    public class ThreeDRotationCalculator
    {
        #region 三维欧拉角计算（ZYX顺序）
        /// <summary>
        /// 计算两组3D点之间的欧拉角（偏航-俯仰-滚动）
        /// </summary>
        public static Vector3 CalculateEulerAngles(List<Vector3> originalPoints, List<Vector3> measuredPoints)
        {
            if (originalPoints.Count != measuredPoints.Count)
                throw new ArgumentException("点集数量必须相同");
            if (originalPoints.Count < 3)
                throw new ArgumentException("至少需要3个点计算三维旋转");

            // 计算质心并中心化点集
            var originalCentroid = CalculateCentroid(originalPoints);
            var measuredCentroid = CalculateCentroid(measuredPoints);

            var originalCentered = originalPoints.Select(p => p - originalCentroid).ToList();
            var measuredCentered = measuredPoints.Select(p => p - measuredCentroid).ToList();

            // 构建3x3协方差矩阵H = ∑(p_i * q_i^T)
            var H = Matrix<double>.Build.Dense(3, 3);
            foreach (var i in Enumerable.Range(0, originalCentered.Count))
            {
                var o = originalCentered[i];
                var m = measuredCentered[i];
                H[0, 0] += o.X * m.X;
                H[0, 1] += o.X * m.Y;
                H[0, 2] += o.X * m.Z;
                H[1, 0] += o.Y * m.X;
                H[1, 1] += o.Y * m.Y;
                H[1, 2] += o.Y * m.Z;
                H[2, 0] += o.Z * m.X;
                H[2, 1] += o.Z * m.Y;
                H[2, 2] += o.Z * m.Z;
            }

            // 执行SVD分解：H = U * Σ * V^T
            var svd = H.Svd();
            var U = svd.U;
            var V = svd.VT.Transpose();

            // 确保旋转矩阵为正交矩阵（处理反射情况）
            if (U.Determinant() * V.Determinant() < 0)
            {
                V = V * DiagonalMatrix(3, 1, 1, -1);
            }

            // 构建旋转矩阵 R = V * U^T
            var rotationMatrix = V * U.Transpose();

            // 从旋转矩阵提取欧拉角（ZYX顺序）
            return RotationMatrixToEulerAngles(rotationMatrix);
        }
        #endregion

        #region 辅助方法
        /// <summary>
        /// 从旋转矩阵提取欧拉角（ZYX顺序，即yaw-pitch-roll）
        /// </summary>
        private static Vector3 RotationMatrixToEulerAngles(Matrix<double> R)
        {
            // 处理数值稳定性（避免除以零）
            double sy = Math.Sqrt(R[0, 0] * R[0, 0] + R[1, 0] * R[1, 0]);
            bool isSingular = sy < 1e-6;

            double roll, pitch, yaw; // X, Y, Z轴旋转角度

            if (!isSingular)
            {
                // 正常情况（非奇异）
                pitch = Math.Atan2(-R[2, 0], sy);
                yaw = Math.Atan2(R[1, 0], R[0, 0]);
                roll = Math.Atan2(R[2, 1], R[2, 2]);
            }
            else
            {
                // 奇异情况（万向节锁）
                pitch = Math.Atan2(-R[2, 0], sy);
                yaw = 0; // Z轴旋转设为0
                roll = Math.Atan2(-R[1, 2], R[1, 1]);
            }

            // 弧度转角度
            return new Vector3(
                (float)(roll * (180.0 / Math.PI)),
                (float)(pitch * (180.0 / Math.PI)),
                (float)(yaw * (180.0 / Math.PI))
            );
        }

        /// <summary>
        /// 计算点集质心（使用Kahan求和算法提高精度）
        /// </summary>
        private static Vector3 CalculateCentroid(List<Vector3> points)
        {
            double sumX = 0, sumY = 0, sumZ = 0;
            double cX = 0, cY = 0, cZ = 0; // Kahan求和补偿项

            foreach (var p in points)
            {
                // X轴累加
                double yX = p.X - cX;
                double tX = sumX + yX;
                cX = tX - sumX - yX;
                sumX = tX;

                // Y轴累加
                double yY = p.Y - cY;
                double tY = sumY + yY;
                cY = tY - sumY - yY;
                sumY = tY;

                // Z轴累加
                double yZ = p.Z - cZ;
                double tZ = sumZ + yZ;
                cZ = tZ - sumZ - yZ;
                sumZ = tZ;
            }

            int count = points.Count;
            return count > 0 ?
                new Vector3((float)(sumX / count), (float)(sumY / count), (float)(sumZ / count)) :
                new Vector3(0, 0, 0);
        }

        /// <summary>
        /// 创建对角矩阵
        /// </summary>
        private static Matrix<double> DiagonalMatrix(int size, params double[] values)
        {
            var diag = Matrix<double>.Build.Dense(size, size);
            for (int i = 0; i < Math.Min(size, values.Length); i++)
            {
                diag[i, i] = values[i];
            }
            return diag;
        }
        #endregion
    }

    public class CoordinateRotationCalculator
    {
        // 默认参数配置
        private const int RansacMinPoints = 4;

        /// <summary>
        /// 计算两组二维坐标点集之间的旋转角度（高精度鲁棒版本）
        /// </summary>
        /// <param name="originalPoints">原始坐标系中的点集</param>
        /// <param name="measuredPoints">测量坐标系中的点集</param>
        /// <param name="options">计算选项</param>
        /// <returns>旋转角度（单位：度，正值表示逆时针旋转）</returns>
        public static double CalculateRotationAngle(
            List<Vector2> originalPoints,
            List<Vector2> measuredPoints,
            RotationCalculationOptions options = null)
        {
            // 验证输入有效性
            ValidateInput(originalPoints, measuredPoints);

            // 使用默认选项
            options ??= new RotationCalculationOptions();

            // 复制点集，避免修改原始数据
            var original = new List<Vector2>(originalPoints);
            var measured = new List<Vector2>(measuredPoints);

            // 移除离群点（可选）
            if (options.RemoveOutliers && original.Count >= RansacMinPoints)
            {
                if (options.UseRansac)
                {
                    RemoveOutliersRANSAC(original, measured, options);
                }
                else
                {
                    RemoveOutliersByErrorThreshold(original, measured, options);
                }
            }

            // 计算初始角度
            double angle = CalculateInitialAngle(original, measured);

            // 迭代优化（可选）
            if (options.MaxIterations > 0)
            {
                angle = RefineRotationAngle(
                    original,
                    measured,
                    angle,
                    options.MaxIterations,
                    options.ConvergenceThreshold);
            }

            return NormalizeAngle(angle);
        }

        private static void ValidateInput(List<Vector2> originalPoints, List<Vector2> measuredPoints)
        {
            if (originalPoints == null || measuredPoints == null)
                throw new ArgumentNullException("点集不能为null");

            if (originalPoints.Count != measuredPoints.Count)
                throw new ArgumentException("点集数量必须相同");

            if (originalPoints.Count < 2)
                throw new ArgumentException("至少需要两个点来计算旋转角度");
        }

        // CoordinateRotationCalculator.cs中优化初始角度计算
        public static double CalculateInitialAngle(List<Vector2> originalPoints, List<Vector2> measuredPoints)
        {

            var originalCentroid = CalculateCentroid(originalPoints);
            var measuredCentroid = CalculateCentroid(measuredPoints);

            // 计算中心化后的点集并构建协方差矩阵
            double sumXX = 0, sumXY = 0, sumYX = 0, sumYY = 0;

            for (int i = 0; i < originalPoints.Count; i++)
            {
                var o = originalPoints[i] - originalCentroid;
                var m = measuredPoints[i] - measuredCentroid;

                sumXX += o.X * m.X;
                sumXY += o.X * m.Y;
                sumYX += o.Y * m.X;
                sumYY += o.Y * m.Y;
            }

            // 直接从协方差矩阵计算旋转角度，无需显式构建矩阵
            double trace = sumXX + sumYY;
            double delta = sumXY - sumYX;

            // 使用四元数方法直接计算旋转角度（更高效）
            double theta = Math.Atan2(delta, trace);

            // 将弧度转换为角度
            return theta * (180.0 / Math.PI);
        }
        /// <summary>
        /// 使用Levenberg-Marquardt算法迭代优化旋转角度
        /// </summary>
        // 在CoordinateRotationCalculator类中优化迭代算法
        private static double RefineRotationAngle(
            List<Vector2> originalPoints,
            List<Vector2> measuredPoints,
            double initialAngle,
            int maxIterations,
            double convergenceThreshold)
        {
            double angleRad = initialAngle * Math.PI / 180.0;
            double lambda = 0.01; // 初始阻尼因子
            double prevError = double.MaxValue;

            // 计算质心
            var originalCentroid = CalculateCentroid(originalPoints);
            var measuredCentroid = CalculateCentroid(measuredPoints);

            // 去中心化
            var originalCentered = originalPoints.Select(p => p - originalCentroid).ToList();
            var measuredCentered = measuredPoints.Select(p => p - measuredCentroid).ToList();

            for (int iter = 0; iter < maxIterations; iter++)
            {
                double sumError = 0;
                double J = 0; // 雅可比矩阵（此处为标量）
                double H = 0; // 黑塞矩阵（此处为标量）

                foreach (var (o, m) in originalCentered.Zip(measuredCentered, (o, m) => (o, m)))
                {
                    double cosTheta = Math.Cos(angleRad);
                    double sinTheta = Math.Sin(angleRad);
                    double rotatedX = o.X * cosTheta - o.Y * sinTheta;
                    double rotatedY = o.X * sinTheta + o.Y * cosTheta;

                    double dx = rotatedX - m.X;
                    double dy = rotatedY - m.Y;
                    double error = dx * dx + dy * dy;
                    sumError += error;

                    // 计算雅可比矩阵元素（误差对角度的导数）
                    double dError_dTheta = 2 * (dx * (-o.X * sinTheta - o.Y * cosTheta) + dy * (o.X * cosTheta - o.Y * sinTheta));
                    J += dError_dTheta;

                    // 计算黑塞矩阵元素（雅可比矩阵的导数）
                    double d2Error_dTheta2 = 2 * (
                        (-o.X * sinTheta - o.Y * cosTheta) * (-o.X * sinTheta - o.Y * cosTheta) +
                        (o.X * cosTheta - o.Y * sinTheta) * (o.X * cosTheta - o.Y * sinTheta) +
                        dx * (-o.X * cosTheta + o.Y * sinTheta) +
                        dy * (-o.X * sinTheta - o.Y * cosTheta)
                    );
                    H += d2Error_dTheta2;
                }

                // 检查收敛
                if (Math.Abs(sumError - prevError) < convergenceThreshold || sumError < convergenceThreshold)
                    break;

                prevError = sumError;

                // Levenberg-Marquardt更新
                double delta = -J / (H + lambda * H);
                angleRad += delta;

                // 自适应调整lambda
                if (delta * J > 0)
                    lambda /= 10;  // 减小阻尼，更接近牛顿法
                else
                    lambda *= 10;  // 增大阻尼，更接近梯度下降

                // 限制lambda范围
                lambda = Math.Max(1e-10, Math.Min(1e10, lambda));
            }

            return angleRad * (180.0 / Math.PI);
        }

        /// <summary>
        /// 使用RANSAC算法移除离群点
        /// </summary>
        public static void RemoveOutliersRANSAC(
            List<Vector2> originalPoints,
            List<Vector2> measuredPoints,
            RotationCalculationOptions options)
        {
            if (originalPoints.Count < RansacMinPoints)
                return;

            int bestInliersCount = 0;
            List<int> bestInliers = new List<int>();
            Random random = new Random();

            // 计算点集的平均距离，用于自适应阈值
            double avgDistance = CalculateAverageDistance(measuredPoints);
            double adaptiveThreshold = options.InlierThreshold * avgDistance;

            for (int iter = 0; iter < options.MaxIterations; iter++)
            {
                // 随机选择至少3个点计算候选旋转角度
                List<int> sampleIndices = new List<int>();
                while (sampleIndices.Count < 3)
                {
                    int idx = random.Next(originalPoints.Count);
                    if (!sampleIndices.Contains(idx))
                        sampleIndices.Add(idx);
                }

                // 计算多点确定的旋转角度（使用平均法）
                double sumAngle = 0;
                int validPairs = 0;

                for (int i = 0; i < sampleIndices.Count; i++)
                {
                    for (int j = i + 1; j < sampleIndices.Count; j++)
                    {
                        int idx1 = sampleIndices[i];
                        int idx2 = sampleIndices[j];

                        Vector2 p1o = originalPoints[idx1];
                        Vector2 p2o = originalPoints[idx2];
                        Vector2 p1m = measuredPoints[idx1];
                        Vector2 p2m = measuredPoints[idx2];

                        double dxo = p2o.X - p1o.X;
                        double dyo = p2o.Y - p1o.Y;
                        double dxm = p2m.X - p1m.X;
                        double dym = p2m.Y - p1m.Y;

                        // 避免除以零
                        if (Math.Abs(dxo) < 1e-10 && Math.Abs(dyo) < 1e-10)
                            continue;

                        double angle = Math.Atan2(dym, dxm) - Math.Atan2(dyo, dxo);
                        sumAngle += angle;
                        validPairs++;
                    }
                }

                if (validPairs == 0)
                    continue;

                double candidateAngle = sumAngle / validPairs;

                // 评估所有点，统计内点
                List<int> inliers = new List<int>();
                for (int i = 0; i < originalPoints.Count; i++)
                {
                    Vector2 o = originalPoints[i];
                    Vector2 m = measuredPoints[i];

                    // 旋转计算（使用质心作为旋转中心）
                    Vector2 centroidO = CalculateCentroid(originalPoints);
                    Vector2 centroidM = CalculateCentroid(measuredPoints);

                    double cosTheta = Math.Cos(candidateAngle);
                    double sinTheta = Math.Sin(candidateAngle);
                    double rotatedX = (o.X - centroidO.X) * cosTheta - (o.Y - centroidO.Y) * sinTheta + centroidM.X;
                    double rotatedY = (o.X - centroidO.X) * sinTheta + (o.Y - centroidO.Y) * cosTheta + centroidM.Y;

                    // 计算误差
                    double error = Math.Sqrt(Math.Pow(rotatedX - m.X, 2) + Math.Pow(rotatedY - m.Y, 2));
                    if (error < adaptiveThreshold)
                        inliers.Add(i);
                }

                // 更新最佳内点集
                if (inliers.Count > bestInliersCount)
                {
                    bestInliersCount = inliers.Count;
                    bestInliers = inliers;
                }
            }

            // 如果找到了足够的内点，保留它们
            if (bestInliersCount > 0 && bestInliersCount < originalPoints.Count * (1 - options.OutlierRejectionRatio))
            {
                var originalInliers = new List<Vector2>();
                var measuredInliers = new List<Vector2>();

                foreach (int idx in bestInliers)
                {
                    originalInliers.Add(originalPoints[idx]);
                    measuredInliers.Add(measuredPoints[idx]);
                }

                originalPoints.Clear();
                measuredPoints.Clear();
                originalPoints.AddRange(originalInliers);
                measuredPoints.AddRange(measuredInliers);
            }
        }

        // 辅助方法：计算点集的平均点间距离
        private static double CalculateAverageDistance(List<Vector2> points)
        {
            if (points.Count < 2)
                return 0;

            double sum = 0;
            int count = 0;

            for (int i = 0; i < points.Count; i++)
            {
                for (int j = i + 1; j < points.Count; j++)
                {
                    double dx = points[i].X - points[j].X;
                    double dy = points[i].Y - points[j].Y;
                    sum += Math.Sqrt(dx * dx + dy * dy);
                    count++;
                }
            }

            return sum / count;
        }

        /// <summary>
        /// 基于误差阈值移除离群点
        /// </summary>
        private static void RemoveOutliersByErrorThreshold(
            List<Vector2> originalPoints,
            List<Vector2> measuredPoints,
            RotationCalculationOptions options)
        {
            // 计算初始旋转角度
            double initialAngle = CalculateInitialAngle(originalPoints, measuredPoints) * Math.PI / 180.0;

            // 计算质心
            var originalCentroid = CalculateCentroid(originalPoints);
            var measuredCentroid = CalculateCentroid(measuredPoints);

            // 去中心化
            var originalCentered = originalPoints.Select(p => p - originalCentroid).ToList();
            var measuredCentered = measuredPoints.Select(p => p - measuredCentroid).ToList();

            // 计算每个点的旋转误差
            double[] errors = new double[originalPoints.Count];
            for (int i = 0; i < originalPoints.Count; i++)
            {
                var o = originalCentered[i];
                var m = measuredCentered[i];

                double cosTheta = Math.Cos(initialAngle);
                double sinTheta = Math.Sin(initialAngle);
                double rotatedX = o.X * cosTheta - o.Y * sinTheta;
                double rotatedY = o.X * sinTheta + o.Y * cosTheta;

                double dx = rotatedX - m.X;
                double dy = rotatedY - m.Y;
                errors[i] = dx * dx + dy * dy;
            }

            // 计算误差的中位数和MAD（中位数绝对偏差）
            double[] sortedErrors = new double[errors.Length];
            Array.Copy(errors, sortedErrors, errors.Length);
            Array.Sort(sortedErrors);
            double medianError = sortedErrors[sortedErrors.Length / 2];

            double[] deviations = new double[errors.Length];
            for (int i = 0; i < errors.Length; i++)
            {
                deviations[i] = Math.Abs(errors[i] - medianError);
            }
            Array.Sort(deviations);
            double mad = deviations[deviations.Length / 2];

            // MAD可能为0（所有误差相同），此时使用标准差
            if (mad < 1e-10)
            {
                double meanError = errors.Average();
                mad = Math.Sqrt(errors.Average(e => Math.Pow(e - meanError, 2)));
            }

            // 计算阈值（中位数 + k*MAD）
            double threshold = medianError + 6 * mad;

            // 移除离群点
            for (int i = originalPoints.Count - 1; i >= 0; i--)
            {
                if (errors[i] > threshold)
                {
                    originalPoints.RemoveAt(i);
                    measuredPoints.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// 使用Kahan求和算法计算质心，提高精度
        /// </summary>
        private static Vector2 CalculateCentroid(List<Vector2> points)
        {
            double sumX = 0, sumY = 0;
            double cX = 0, cY = 0; // Kahan求和补偿项

            foreach (var p in points)
            {
                double yX = p.X - cX;
                double tX = sumX + yX;
                cX = tX - sumX - yX;
                sumX = tX;

                double yY = p.Y - cY;
                double tY = sumY + yY;
                cY = tY - sumY - yY;
                sumY = tY;
            }

            int count = points.Count;
            return count > 0 ? new Vector2((float)(sumX / count), (float)(sumY / count)) : Vector2.Zero;
        }

        /// <summary>
        /// 将角度规范化到指定范围内
        /// </summary>
        private static double NormalizeAngle(double angle)
        {
            // 首先规范化到0-360度
            angle = angle % 360;
            if (angle < 0) angle += 360;

            return angle;
        }
    }

    /// <summary>
    /// 坐标旋转计算选项
    /// </summary>
    public class RotationCalculationOptions
    {
        /// <summary>
        /// 是否移除离群点
        /// </summary>
        public bool RemoveOutliers { get; set; } = true;

        /// <summary>
        /// 是否使用RANSAC算法进行离群点检测
        /// </summary>
        public bool UseRansac { get; set; } = true;

        /// <summary>
        /// 内点阈值（用于RANSAC）
        /// </summary>
        public double InlierThreshold { get; set; } = 0.5;

        /// <summary>
        /// 离群点拒绝比例阈值
        /// </summary>
        public double OutlierRejectionRatio { get; set; } = 0.3;

        /// <summary>
        /// 迭代优化的最大次数
        /// </summary>
        public int MaxIterations { get; set; } = 10;

        /// <summary>
        /// 收敛阈值
        /// </summary>
        public double ConvergenceThreshold { get; set; } = 1e-12;
    }
}