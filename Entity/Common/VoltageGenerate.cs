using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.Common
{
    public class VoltageGenerator
    {
        public static double[] GenerateVoltageSequence(int numPoints, double accelRatio, double decelRatio,
            double startVoltage, double endVoltage, double startStrength = 5.0, double endStrength = 5.0)
        {
            // 确保加速段和减速段占比之和不超过1
            double totalRatio = accelRatio + decelRatio;
            if (totalRatio > 1)
            {
                double scale = 1 / totalRatio;
                accelRatio *= scale;
                decelRatio *= scale;
            }

            // 创建归一化的位置数组 [0, 1]
            double[] x = new double[numPoints];
            for (int i = 0; i < numPoints; i++)
            {
                x[i] = (double)i / (numPoints - 1);
            }

            // 基础线性序列
            double[] baseSequence = new double[numPoints];
            Array.Copy(x, baseSequence, numPoints);

            // 调整起始部分（使用五次函数）
            if (accelRatio > 0)
            {
                for (int i = 0; i < numPoints; i++)
                {
                    if (x[i] < accelRatio)
                    {
                        double t = x[i] / accelRatio;  // 归一化到 [0,1]
                                                       // 应用五次过渡函数，确保从0开始
                        baseSequence[i] = accelRatio * QuinticTransition(t, startStrength);
                    }
                }
            }

            // 调整结束部分（使用五次函数）
            if (decelRatio > 0)
            {
                for (int i = 0; i < numPoints; i++)
                {
                    if (x[i] > (1 - decelRatio))
                    {
                        double t = (x[i] - (1 - decelRatio)) / decelRatio;  // 归一化到 [0,1]
                                                                            // 应用五次过渡函数并转换到正确范围，确保在1结束
                        baseSequence[i] = (1 - decelRatio) + decelRatio * QuinticTransition(t, endStrength);
                    }
                }
            }

            // 映射到起始和终止电压范围
            double[] voltageSequence = new double[numPoints];
            for (int i = 0; i < numPoints; i++)
            {
                voltageSequence[i] = startVoltage + (endVoltage - startVoltage) * baseSequence[i];
            }

            // 如果序列方向相反，则翻转
            if (voltageSequence[0] == endVoltage)
            {
                Array.Reverse(voltageSequence);
            }

            return voltageSequence;
        }

        private static double QuinticTransition(double t, double strength)
        {
            /* 五次函数过渡: 6t⁵ - 15t⁴ + 10t³，确保在t=0和t=1处导数为0 */
            // 调整强度参数
            double k = Math.Max(1.0, strength);
            // 五次函数确保在端点处斜率为0，过渡更平滑
            return 6 * Math.Pow(t, k) - 15 * Math.Pow(t, k * 4 / 5) + 10 * Math.Pow(t, k * 3 / 5);
        }

        public void Voltest()
        {
            // 示例1：生成从0V到5V的电压序列，加速段占20%，减速段占30%
            double[] sequence1 = VoltageGenerator.GenerateVoltageSequence(
                100,        // 点数
                0.2,        // 加速段占比
                0.3,        // 减速段占比
                0.0,        // 起始电压
                5.0         // 终止电压
            );

            // 示例2：生成从3V到-2V的电压序列，加速段占10%，减速段占10%，使用更高的强度参数
            double[] sequence2 = VoltageGenerator.GenerateVoltageSequence(
                200,        // 点数
                0.1,        // 加速段占比
                0.1,        // 减速段占比
                3.0,        // 起始电压
                -2.0,       // 终止电压
                8.0,        // 起始强度
                8.0         // 终止强度
            );

            // 打印示例1的前10个点
            Console.WriteLine("示例1的前10个电压值：");
            for (int i = 0; i < sequence1.Length; i++)
            {
                Console.WriteLine($"点 {i}: {sequence1[i]:F4}V");
            }

            // 打印示例2的前10个点
            Console.WriteLine("\n示例2的前10个电压值：");
            for (int i = 0; i < sequence2.Length; i++)
            {
                Console.WriteLine($"点 {i}: {sequence2[i]:F4}V");
            }
        }
    }
}
