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
    }
}
