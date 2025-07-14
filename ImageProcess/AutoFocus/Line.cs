using OpenCvSharp.Extensions;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Point = OpenCvSharp.Point;

namespace AutoFocus
{
    internal class Line
    {
        public void LineP(string path = null, string offvalue = null)
        {
            // 设置命令行参数：图像目录和偏移值
            string[] args = { "D:/111", "1684" };

            // 检查参数数量是否足够
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: ImageProcessingApp <image_directory> <offset_value>");
                return;
            }

            // 解析参数
            string imageDirectory = args[0];
            int offsetValue = int.Parse(args[1]);
            List<Mat> arrayImg = new List<Mat>();

            // 读取目录中的所有图像
            ReadImages(imageDirectory, arrayImg);

            // 处理每张图像
            foreach (var img in arrayImg)
            {
                using (Mat grayImage = new Mat())
                {
                    // 将图像转换为灰度图
                    Cv2.CvtColor(img, grayImage, ColorConversionCodes.BGR2GRAY);

                    // 获取图像尺寸
                    int h = grayImage.Rows;
                    int w = grayImage.Cols;

                    // 计算图像中心坐标
                    Point center = new Point(w / 2, h / 2);

                    // 旋转并裁剪图像（这里旋转角度为0，主要用于裁剪）
                    var result = RotateAndCropImage(grayImage, center, 0);
                    Mat rotated = result.Item1;      // 旋转并裁剪后的图像
                    Point offset = result.Item2;     // 偏移量
                    Func<int, int, (int, int)> mapCoords = result.Item3;  // 坐标映射函数

                    int hSize = rotated.Rows;
                    int wSize = rotated.Cols;

                    // 计算每一行的平均灰度值，得到一维数组
                    double[] y = new double[hSize];
                    for (int i = 0; i < hSize; i++)
                    {
                        float sum = 0;
                        for (int j = 0; j < wSize; j++)
                        {
                            sum += rotated.At<byte>(i, j);
                        }
                        y[i] = sum / wSize;
                    }

                    // 计算平均值作为阈值参考
                    double yMean = y.Average();

                    // 找到局部极小值点（灰度值较低的区域）
                    List<int> localMinima = FindLocalMinima(y);

                    // 筛选有效的局部极小值点
                    List<int> validMinima = CheckMinima(localMinima, y, yMean);

                    bool istrue = false;  // 是否找到有效的点
                    int middle = 0;       // 中心点位置
                    (int xOri, int yOri) = (-1, -1);  // 最终标记点坐标

                    // 根据有效极小值点的数量进行不同处理
                    if (validMinima.Count > 2)
                    {
                        // 处理错误情况：找到过多极小值点
                    }
                    else if (validMinima.Count == 2)
                    {
                        istrue = true;
                        // 如果两个极小值点距离过远，取平均值
                        if (Math.Abs(validMinima[0] - validMinima[1]) > 1000)
                        {
                            middle = (int)y.Average();
                        }
                    }
                    else if (validMinima.Count == 1)
                    {
                        istrue = true;
                        // 根据极小值点位置和偏移值计算中心点
                        if (validMinima[0] < h / 2)
                        {
                            middle = validMinima[0] + offsetValue; // 1750 聚乙酰胺   1685 康宁
                        }
                        else
                        {
                            middle = validMinima[0] - offsetValue;
                        }
                    }
                    else if (validMinima.Count == 0)
                    {
                        // 处理无极小值的情况
                    }

                    // 如果找到有效点，计算其在原图中的坐标
                    if (istrue)
                    {
                        (xOri, yOri) = mapCoords(wSize / 2, middle);
                    }

                    // 输出标记点坐标
                    Console.WriteLine($"{xOri}, {yOri}");

                    // 在原图上绘制标记并保存
                    using (Bitmap bitmap = BitmapConverter.ToBitmap(img))
                    using (Graphics g = Graphics.FromImage(bitmap))
                    {
                        // 绘制红色圆形标记
                        g.DrawEllipse(new Pen(Color.Red, 5), xOri - 5, yOri - 5, 10, 10);

                        // 确保输出目录存在
                        string outputDir = Path.GetDirectoryName(imageDirectory);
                        if (!Directory.Exists(outputDir))
                        {
                            Directory.CreateDirectory(outputDir);
                            Console.WriteLine($"创建目录: {outputDir}");
                        }

                        // 生成唯一的输出文件名并保存图像
                        string outputPath = Path.Combine(outputDir, $"processed_{Path.GetRandomFileName()}.jpg");
                        bitmap.Save(outputPath, ImageFormat.Jpeg);
                        Console.WriteLine($"保存图像: {outputPath}");
                    }
                }
            }
        }

        // 从指定目录读取所有图像文件
        static void ReadImages(string directory, List<Mat> arrayOfImg)
        {
            foreach (string filename in Directory.GetFiles(directory))
            {
                if (IsImageFile(filename))
                {
                    Mat img = Cv2.ImRead(filename, ImreadModes.Color);
                    if (!img.Empty())
                    {
                        arrayOfImg.Add(img);
                    }
                }
            }
        }

        // 判断文件是否为图像文件
        static bool IsImageFile(string filename)
        {
            string extension = Path.GetExtension(filename).ToLower();
            return extension == ".jpg" || extension == ".jpeg" || extension == ".png" ||
                   extension == ".bmp" || extension == ".tif" || extension == ".tiff";
        }

        // 旋转并裁剪图像，返回处理后的图像、偏移量和坐标映射函数
        static (Mat, Point, Func<int, int, (int, int)>) RotateAndCropImage(Mat image, Point center, double angle)
        {
            // 获取图像尺寸
            int h = image.Rows;
            int w = image.Cols;

            // 计算旋转矩阵
            Mat rotMat = Cv2.GetRotationMatrix2D(center, angle, 1.0);

            // 计算旋转后的图像边界
            double cos = Math.Abs(rotMat.At<double>(0, 0));
            double sin = Math.Abs(rotMat.At<double>(0, 1));

            // 计算旋转后图像的新尺寸
            int newW = (int)((h * sin) + (w * cos));
            int newH = (int)((h * cos) + (w * sin));

            // 调整旋转矩阵以考虑平移，确保图像居中
            rotMat.At<double>(0, 2) += (newW / 2) - center.X;
            rotMat.At<double>(1, 2) += (newH / 2) - center.Y;

            // 执行仿射变换（旋转）
            Mat rotated = new Mat();
            Cv2.WarpAffine(image, rotated, rotMat, new OpenCvSharp.Size(newW, newH));

            // 计算裁剪区域，去除旋转后产生的黑色边缘
            int x1 = (int)(newW / 2 - (w * cos - h * sin) / 2);
            int y1 = (int)(newH / 2 - (h * cos + w * sin) / 2);
            int x2 = (int)(newW / 2 + (w * cos - h * sin) / 2);
            int y2 = (int)(newH / 2 + (h * cos + w * sin) / 2);

            // 裁剪图像
            Rect roi = new Rect(x1, y1, x2 - x1, y2 - y1);
            Mat cropped = rotated[roi].Clone();

            // 计算坐标偏移量
            int dx = (w - (x2 - x1)) / 2;
            int dy = (h - (y2 - y1)) / 2;

            // 坐标映射函数：将裁剪后图像中的坐标映射回原图
            Func<int, int, (int, int)> mapCoords = (x, y) =>
            {
                int xOrig = (int)((x - (newW / 2 - dx)) * cos + (y - (newH / 2 - dy)) * sin + center.X - dx);
                int yOrig = (int)(-(x - (newW / 2 - dx)) * sin + (y - (newH / 2 - dy)) * cos + center.Y - dy);
                return (xOrig, yOrig);
            };

            return (cropped, new Point(dx, dy), mapCoords);
        }

        // 查找数组中的局部极小值点
        static List<int> FindLocalMinima(double[] arr)
        {
            List<int> minima = new List<int>();
            for (int i = 1; i < arr.Length - 1; i++)
            {
                // 如果当前点的值小于其左右相邻点的值，则为局部极小值
                if (arr[i] < arr[i - 1] && arr[i] < arr[i + 1])
                {
                    minima.Add(i);
                }
            }
            return minima;
        }

        // 检查并筛选有效的局部极小值点
        static List<int> CheckMinima(List<int> minima, double[] arr, double meanValue, int windowSize = 80)
        {
            List<int> validMinima = new List<int>();
            foreach (int idx in minima)
            {
                // 确定局部窗口范围
                int start = Math.Max(0, idx - windowSize / 2);
                int end = Math.Min(arr.Length, idx + windowSize / 2 + 1);

                // 提取局部窗口数据
                double[] localMinWindow = new double[end - start];
                Array.Copy(arr, start, localMinWindow, 0, end - start);

                // 判断条件：
                // 1. 当前点的值小于平均值
                // 2. 当前点是局部窗口中的最小值
                // 3. 局部窗口中的最大值与最小值之比大于1.2（确保对比度足够）
                if (arr[idx] < meanValue && arr[idx] == localMinWindow.Min() && localMinWindow.Max() / localMinWindow.Min() > 1.2)
                {
                    validMinima.Add(idx);
                }
            }
            return validMinima;
        }
    }
}
