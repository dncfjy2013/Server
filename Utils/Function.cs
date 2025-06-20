namespace Server.Utils
{
    public class Function
    {
        public static string FormatBytes(long bytes)
        {
            const int scale = 1024;
            string[] orders = { "Bytes", "KB", "MB", "GB", };
            int max = orders.Length - 1;

            double result = bytes;
            int order = 0;
            while (result >= scale && order < max)
            {
                result /= scale;
                order++;
            }

            return $"{result:0.##} {orders[order]}";
        }

        public static List<double> Resample(List<double> input, double scaleFactor)
        {
            if (input == null || input.Count == 0)
                return new List<double>();

            if (scaleFactor <= 0)
                throw new ArgumentException("缩放因子必须大于0", nameof(scaleFactor));

            if (Math.Abs(scaleFactor - 1.0) < 1e-9) // 基本等于1，无需调整
                return new List<double>(input);

            int originalLength = input.Count;
            int targetLength = Math.Max(1, (int)Math.Round(originalLength * scaleFactor));
            List<double> result = new List<double>(targetLength);

            for (int i = 0; i < targetLength; i++)
            {
                // 调整映射公式，确保从第一个值开始计算
                double scale = (double)(originalLength) / targetLength;
                int originalIndex = Math.Min(originalLength - 1, (int)(i * scale));
                result.Add(input[originalIndex]);
            }

            return result;
        }
    }
}
