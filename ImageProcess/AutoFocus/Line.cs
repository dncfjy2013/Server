using OpenCvSharp.Extensions;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.Drawing;
using System.Linq;
using System.IO;
using Point = OpenCvSharp.Point;

namespace AutoFocus
{
    public class AutoFocusConfig
    {
        public string ImageDirectory { get; set; } = "D:/111";
        public int OffsetValue { get; set; } = 1684;
        public int MinWindowSize { get; set; } = 80;
        public float MinContrastRatio { get; set; } = 1.2f;
        public int MaxValidMinimaDistance { get; set; } = 1000;
        public string OutputImageFormat { get; set; } = "jpg";
    }

    public class Line
    {
        private readonly AutoFocusConfig _config;

        public Line(AutoFocusConfig config)
        {
            _config = config;
        }

        public void Process()
        {
            List<Mat> arrayImg = new List<Mat>();
            ReadImages(_config.ImageDirectory, arrayImg);

            foreach (var img in arrayImg)
            {
                using Mat grayImage = new Mat();
                Cv2.CvtColor(img, grayImage, ColorConversionCodes.BGR2GRAY);

                int h = grayImage.Rows;
                int w = grayImage.Cols;
                Point center = new Point(w / 2, h / 2);

                var (rotated, offset, mapCoords) = RotateAndCropImage(grayImage, center, 0);

                int hSize = rotated.Rows;
                int wSize = rotated.Cols;

                double[] y = new double[hSize];
                for (int i = 0; i < hSize; i++)
                {
                    float sum = 0;
                    for (int j = 0; j < wSize; j++)
                        sum += rotated.At<byte>(i, j);

                    y[i] = sum / wSize;
                }

                double yMean = y.Average();
                List<int> localMinima = FindLocalMinima(y);
                List<int> validMinima = CheckMinima(localMinima, y, yMean, _config.MinWindowSize, _config.MinContrastRatio);

                bool istrue = false;
                int middle = 0;
                (int xOri, int yOri) = (-1, -1);

                if (validMinima.Count == 2)
                {
                    istrue = true;
                    if (Math.Abs(validMinima[0] - validMinima[1]) > _config.MaxValidMinimaDistance)
                        middle = (int)y.Average();
                }
                else if (validMinima.Count == 1)
                {
                    istrue = true;
                    if (validMinima[0] < h / 2)
                        middle = validMinima[0] + _config.OffsetValue;
                    else
                        middle = validMinima[0] - _config.OffsetValue;
                }

                if (istrue)
                    (xOri, yOri) = mapCoords(wSize / 2, middle);

                Console.WriteLine($"{xOri}, {yOri}");

                using Bitmap bitmap = BitmapConverter.ToBitmap(img);
                using Graphics g = Graphics.FromImage(bitmap);
                g.DrawEllipse(new Pen(Color.Red, 5), xOri - 5, yOri - 5, 10, 10);

                string outputDir = Path.GetDirectoryName(_config.ImageDirectory);
                if (!Directory.Exists(outputDir))
                    Directory.CreateDirectory(outputDir);

                string outputPath = Path.Combine(outputDir, $"processed_{Path.GetRandomFileName()}.{_config.OutputImageFormat}");
                bitmap.Save(outputPath, ImageFormat.Jpeg);
            }
        }

        static void ReadImages(string directory, List<Mat> arrayOfImg)
        {
            foreach (string filename in Directory.GetFiles(directory))
            {
                if (IsImageFile(filename))
                {
                    Mat img = Cv2.ImRead(filename, ImreadModes.Color);
                    if (!img.Empty())
                        arrayOfImg.Add(img);
                }
            }
        }

        static bool IsImageFile(string filename)
        {
            string ext = Path.GetExtension(filename).ToLower();
            return ext is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".tif" or ".tiff";
        }

        static (Mat, Point, Func<int, int, (int, int)>) RotateAndCropImage(Mat image, Point center, double angle)
        {
            int h = image.Rows;
            int w = image.Cols;

            Mat rotMat = Cv2.GetRotationMatrix2D(center, angle, 1.0);
            double cos = Math.Abs(rotMat.At<double>(0, 0));
            double sin = Math.Abs(rotMat.At<double>(0, 1));

            int newW = (int)((h * sin) + (w * cos));
            int newH = (int)((h * cos) + (w * sin));

            rotMat.At<double>(0, 2) += (newW / 2) - center.X;
            rotMat.At<double>(1, 2) += (newH / 2) - center.Y;

            Mat rotated = new Mat();
            Cv2.WarpAffine(image, rotated, rotMat, new OpenCvSharp.Size(newW, newH));

            int x1 = (int)(newW / 2 - (w * cos - h * sin) / 2);
            int y1 = (int)(newH / 2 - (h * cos + w * sin) / 2);
            int x2 = (int)(newW / 2 + (w * cos - h * sin) / 2);
            int y2 = (int)(newH / 2 + (h * cos + w * sin) / 2);

            Rect roi = new Rect(x1, y1, x2 - x1, y2 - y1);
            Mat cropped = rotated[roi].Clone();

            int dx = (w - (x2 - x1)) / 2;
            int dy = (h - (y2 - y1)) / 2;

            Func<int, int, (int, int)> mapCoords = (x, y) =>
            {
                int xOrig = (int)((x - (newW / 2 - dx)) * cos + (y - (newH / 2 - dy)) * sin + center.X - dx);
                int yOrig = (int)(-(x - (newW / 2 - dx)) * sin + (y - (newH / 2 - dy)) * cos + center.Y - dy);
                return (xOrig, yOrig);
            };

            return (cropped, new Point(dx, dy), mapCoords);
        }

        static List<int> FindLocalMinima(double[] arr)
        {
            List<int> minima = new List<int>();
            for (int i = 1; i < arr.Length - 1; i++)
            {
                if (arr[i] < arr[i - 1] && arr[i] < arr[i + 1])
                    minima.Add(i);
            }
            return minima;
        }

        static List<int> CheckMinima(List<int> minima, double[] arr, double meanValue, int windowSize, float minRatio)
        {
            List<int> validMinima = new List<int>();
            foreach (int idx in minima)
            {
                int start = Math.Max(0, idx - windowSize / 2);
                int end = Math.Min(arr.Length, idx + windowSize / 2 + 1);

                double[] local = new double[end - start];
                Array.Copy(arr, start, local, 0, end - start);

                if (arr[idx] < meanValue && arr[idx] == local.Min() && local.Max() / local.Min() > minRatio)
                    validMinima.Add(idx);
            }
            return validMinima;
        }
    }
}