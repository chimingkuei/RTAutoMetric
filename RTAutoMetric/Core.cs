using OpenCvSharp.Extensions;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Net.NetworkInformation;
using Tesseract;
using System.Windows.Shapes;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using Newtonsoft.Json;
using System.Windows;
using System.Windows.Media.Media3D;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics;
using Point = OpenCvSharp.Point;
using MathNet.Numerics.RootFinding;
using Path = System.IO.Path;


namespace RTAutoMetric
{
    class Core
    {
        TesseractEngine ocr;
        public string outputFolder;
        public string fileName;
        public string fileExtension;

        private BitmapImage ConvertBitmapToBitmapImage(Bitmap bitmap)
        {
            using (MemoryStream memory = new MemoryStream())
            {
                bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
                memory.Position = 0;
                BitmapImage bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.StreamSource = memory;
                bitmapImage.EndInit();
                bitmapImage.Freeze();
                return bitmapImage;
            }
        }

        public T CaptureRegion<T>(int x, int y, int width, int height, bool show_cursor = false) where T : class
        {
            using (Bitmap bitmap = new Bitmap(width, height))
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(width, height));
                    if (show_cursor)
                    {
                        System.Drawing.Point cursorPosition = System.Windows.Forms.Cursor.Position;
                        System.Windows.Forms.Cursor cursor = System.Windows.Forms.Cursors.Default;
                        System.Drawing.Rectangle cursorBounds = new System.Drawing.Rectangle(cursorPosition, cursor.Size);
                        cursor.Draw(g, cursorBounds);
                    }
                }
                switch (typeof(T))
                {
                    case Type t when t == typeof(BitmapImage):
                        return ConvertBitmapToBitmapImage(bitmap) as T;
                    case Type t when t == typeof(Mat):
                        return BitmapConverter.ToMat(bitmap) as T;
                    case Type t when t == typeof(Bitmap):
                        return bitmap as T;
                    default:
                        throw new NotSupportedException($"Type {typeof(T)} is not supported.");
                }
            }
        }

        public void OCR<T>(T src, string language, out string result)
        {
            result = null;
            ocr = new TesseractEngine("./tessdata", language, EngineMode.Default);
            Tesseract.Page page = null;
            switch (src)
            {
                case Bitmap bitmap:
                    page = ocr.Process(bitmap);
                    break;

                case string filePath when File.Exists(filePath):
                    page = ocr.Process(Pix.LoadFromFile(filePath));
                    break;
            }
            if (page != null)
            {
                result = page.GetText();
                page.Dispose();
            }
        }

        public Mat ConvertBinaryInv(Mat img, double threshold)
        {
            Mat grayImg = new Mat();
            Cv2.CvtColor(img, grayImg, ColorConversionCodes.BGR2GRAY);
            Mat binaryInv = new Mat();
            Cv2.Threshold(grayImg, binaryInv, threshold, 255, ThresholdTypes.BinaryInv);
            return binaryInv;
        }

        #region Fit Line
        private bool IsWhite(Mat src, Point p)
        {
            if (p.X < 0 || p.X >= src.Cols || p.Y < 0 || p.Y >= src.Rows)
                return false;
            return src.At<byte>(p.Y, p.X) == 255;
        }

        private Point? FLNearestWhite(Mat src, Point start, int nx, int ny, int maxDist, bool direction)
        {
            int x = start.X;
            int y = start.Y;
            int step = direction ? 1 : -1;
            for (int d = 1; d <= maxDist; d++)
            {
                x = start.X + d * nx * step;
                y = start.Y + d * ny * step;
                if (x < 0 || x >= src.Width || y < 0 || y >= src.Height)
                    break;
                Point p = new Point(x, y);
                if (IsWhite(src, p))
                    return p;
            }
            return null;
        }

        private (Point startPoint, Point endPoint) FitLine(List<Point> points, Mat src, bool extendToBorders)
        {
            // 轉換影像座標點為數學坐標系 (Y = img.Height - Y)
            var x = points.Select(p => (double)p.X).ToArray();
            var y = points.Select(p => (double)(src.Height - p.Y)).ToArray();
            // 使用 MathNet.Numerics 進行最小二乘法擬合直線
            var result = Fit.Line(x, y);
            double intercept = result.Item1; // 截距
            double slope = result.Item2; // 斜率
            // 輸出斜率和截距
            Console.WriteLine($"斜率: {slope}");
            Console.WriteLine($"截距: {intercept}");
            // 計算 x 範圍
            double xMin, xMax;
            if (extendToBorders)
            {
                // 影像的左右邊界
                xMin = 0;
                xMax = src.Width - 1;
            }
            else
            {
                // 只在輸入點的範圍內畫線
                xMin = x.Min();
                xMax = x.Max();
            }
            // 根據擬合的直線公式計算對應的 y 值
            double yMinMath = slope * xMin + intercept;
            double yMaxMath = slope * xMax + intercept;
            // 轉換數學座標系的 y 值回影像座標系 (Y = img.Height - Y)
            double yMin = src.Height - yMinMath;
            double yMax = src.Height - yMaxMath;
            // 確保 y 值在影像範圍內
            yMin = Math.Max(0, Math.Min(yMin, src.Height - 1));
            yMax = Math.Max(0, Math.Min(yMax, src.Height - 1));
            return (new Point((int)xMin, (int)yMin), new Point((int)xMax, (int)yMax));
        }

        private double Cal2PtDist(Point p1, Point p2)
        {
            return Math.Sqrt(Math.Pow(p1.X - p2.X, 2) + Math.Pow(p1.Y - p2.Y, 2));
        }

        private List<Point> FLOutlier(List<Point> points, double k)
        {
            if (k == -1)
                return points;
            // 計算每個點與其他點的距離
            List<double> distances = new List<double>();
            for (int i = 0; i < points.Count; i++)
            {
                for (int j = i + 1; j < points.Count; j++)
                {
                    double distance = Cal2PtDist(points[i], points[j]);
                    distances.Add(distance);
                }
            }
            // 計算平均距離和標準差
            double meanDistance = distances.Average();
            double stdDevDistance = Math.Sqrt(distances.Average(d => Math.Pow(d - meanDistance, 2)));
            // 計算閾值
            double threshold = meanDistance + k * stdDevDistance;
            // 過濾離群點，根據與其他點的平均距離來剔除
            var filteredPoints = points.Where(p =>
            {
                // 計算當前點與其他點的平均距離
                double avgDistance = points.Where(other => !other.Equals(p))
                                            .Average(other => Cal2PtDist(p, other));
                return avgDistance <= threshold;  // 只保留在閾值內的點
            }).ToList();
            return filteredPoints;
        }

        public void FLByWhiteDot(Mat src, int threshold, Tuple<Point, Point> line, int stepSize, int maxDist, bool direction, double k, bool saveImg = true)
        {
            Mat binaryInv = ConvertBinaryInv(src, threshold);
            if (saveImg)
                Cv2.ImWrite(Path.Combine(outputFolder, Path.GetFileNameWithoutExtension(fileName) + "_binaryInv" + fileExtension), binaryInv);
            List<Point> fitLinePoints = new List<Point>();
            List<Point> filterLinePoints;
            Point p1 = line.Item1;
            Point p2 = line.Item2;
            int dx = p2.X - p1.X;
            int dy = p2.Y - p1.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            // 計算法向量（確保方向正確）
            double nxf = -dy / length;
            double nyf = dx / length;
            int nx = (int)Math.Round(nxf);
            int ny = (int)Math.Round(nyf);
            // 沿著線段取樣
            int steps = (int)(length / stepSize);
            for (int i = 0; i <= steps; i++)
            {
                int x = p1.X + i * dx / steps;
                int y = p1.Y + i * dy / steps;
                Point start = new Point(x, y);
                // 標記採樣點
                if (saveImg)
                    Cv2.Circle(src, start, 2, Scalar.Green, -1);
                // 前進尋找白點
                Point? whitePoint = FLNearestWhite(binaryInv, start, nx, ny, maxDist, direction);
                if (whitePoint.HasValue)
                {
                    fitLinePoints.Add(whitePoint.Value);
                    Cv2.Circle(src, whitePoint.Value, 3, Scalar.Red, -1);
                }
                Point end = new Point(start.X + (direction ? 1 : -1) * maxDist * nx,
                                      start.Y + (direction ? 1 : -1) * maxDist * ny);
                // 畫出搜尋路徑 (黃色)
                if (saveImg)
                    Cv2.Line(src, start, end, Scalar.Yellow, 1);
            }
            filterLinePoints = FLOutlier(fitLinePoints, k);
            if (filterLinePoints.Count < 2)
            {
                Console.WriteLine("Not enough points to fit a line.");
                return;
            }
            (Point startPoint, Point endPoint)= FitLine(filterLinePoints, src, direction);
            if (saveImg)
            {
                Cv2.Line(src, startPoint, endPoint, Scalar.Blue, 2);
                Cv2.ImWrite(Path.Combine(outputFolder, Path.GetFileNameWithoutExtension(fileName) + "_Result" + fileExtension), src);
            }
        }
        #endregion

        #region Fit Circle
        private double Cal2PtDist(PointF p1, PointF p2)
        {
            return Math.Sqrt(Math.Pow(p1.X - p2.X, 2) + Math.Pow(p1.Y - p2.Y, 2));
        }

        private void FitCircle(List<PointF> pointFs, out float CenterX, out float CenterY, out float CenterR)
        {
            Matrix<float> YMat;
            Matrix<float> RMat;
            Matrix<float> AMat;
            List<float> YLit = new List<float>();
            List<float[]> RLit = new List<float[]>();
            // 構建Y矩陣
            foreach (var pointF in pointFs)
                YLit.Add(pointF.X * pointF.X + pointF.Y * pointF.Y);
            float[,] Yarray = new float[YLit.Count, 1];
            for (int i = 0; i < YLit.Count; i++)
                Yarray[i, 0] = YLit[i];
            YMat = CreateMatrix.DenseOfArray<float>(Yarray);
            // 構建R矩陣
            foreach (var pointF in pointFs)
                RLit.Add(new float[] { -pointF.X, -pointF.Y, -1 });
            float[,] Rarray = new float[RLit.Count, 3];
            for (int i = 0; i < RLit.Count; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    Rarray[i, j] = RLit[i][j];
                }
            }
            RMat = CreateMatrix.DenseOfArray<float>(Rarray);
            Matrix<float> RTMat = RMat.Transpose();
            Matrix<float> RRTInvMat = (RTMat.Multiply(RMat)).Inverse();
            AMat = RRTInvMat.Multiply(RTMat.Multiply(YMat));
            float[,] Aarray = AMat.ToArray();
            float A = Aarray[0, 0];
            float B = Aarray[1, 0];
            float C = Aarray[2, 0];
            CenterX = A / -2.0f;
            CenterY = B / -2.0f;
            CenterR = (float)(Math.Sqrt((A * A + B * B - 4 * C)) / 2.0f);
        }

        /// <summary>
        /// k值調整：較小k值（例如k = 1.0），過濾更多點；較大k值（例如k = 3.0）會保留更多點，只過濾掉極端離群點。
        /// 過濾機制：若某個點與其它點的平均距離大於mean + k * std，則該點被視為離群點並被剔除。
        /// </summary>
        private List<PointF> FCOutlier(List<PointF> points, double k)
        {
            if (k == -1)
                return points;
            // 計算每個點與其他點的距離
            List<double> distances = new List<double>();
            for (int i = 0; i < points.Count; i++)
            {
                for (int j = i + 1; j < points.Count; j++)
                {
                    double distance = Cal2PtDist(points[i], points[j]);
                    distances.Add(distance);
                }
            }
            // 計算平均距離和標準差
            double meanDistance = distances.Average();
            double stdDevDistance = Math.Sqrt(distances.Average(d => Math.Pow(d - meanDistance, 2)));
            // 計算閾值
            double threshold = meanDistance + k * stdDevDistance;
            // 過濾離群點，根據與其他點的平均距離來剔除
            var filteredPoints = points.Where(p =>
            {
                // 計算當前點與其他點的平均距離
                double avgDistance = points.Where(other => !other.Equals(p))
                                            .Average(other => Cal2PtDist(p, other));
                return avgDistance <= threshold;  // 只保留在閾值內的點
            }).ToList();
            return filteredPoints;
        }

        /// <summary>
        /// direction-->true:由內而外找白點;false:由外而內找白點
        /// </summary>
        public bool FCByWhiteDot(Mat src, int threshold, Point center, int Radius, bool direction, double k, bool saveImg = true)
        {
            Mat binaryInv = ConvertBinaryInv(src, threshold);
            if (saveImg)
                Cv2.ImWrite(Path.Combine(outputFolder, Path.GetFileNameWithoutExtension(fileName) + "_binaryInv" + fileExtension), binaryInv);
            List<PointF> fitCirclePoints = new List<PointF>();
            List<PointF> filterCirclePoints;
            for (int angle = 1; angle <= 360; angle++)
            {
                int start = direction ? 1 : Radius;
                int end = direction ? Radius : 1;
                int step = direction ? 1 : -1;
                for (int i = start; direction ? i <= end : i >= end; i += step)
                {
                    int x = Convert.ToInt32(center.X + i * Math.Cos((Math.PI / 180) * angle));
                    int y = Convert.ToInt32(center.Y - i * Math.Sin((Math.PI / 180) * angle));
                    if (binaryInv.At<byte>(y, x) == 255) // color use At<Vec3b>(y, x)
                    {
                        fitCirclePoints.Add(new PointF(x, src.Height - y)); // 轉換為笛卡爾座標
                        break;
                    }
                }
            }
            filterCirclePoints = FCOutlier(fitCirclePoints, k);
            if (filterCirclePoints.Count >= 3)
            {
                if (saveImg)
                {
                    // 在影像上標記過濾後的點(轉回影像座標)
                    foreach (var point in filterCirclePoints)
                    {
                        int imgX = (int)point.X;
                        int imgY = src.Height - (int)point.Y;
                        Cv2.Circle(src, imgX, imgY, 1, Scalar.Blue, 1);
                    }
                }
                float CenterX, CenterY, CenterR;
                FitCircle(filterCirclePoints, out CenterX, out CenterY, out CenterR);
                if (saveImg)
                {
                    // 轉回影像座標
                    int imgCenterY = src.Height - (int)CenterY;
                    Cv2.Circle(src, new Point((int)CenterX, imgCenterY), 5, Scalar.Green, -1);
                    Cv2.Circle(src, new Point((int)CenterX, imgCenterY), (int)CenterR, Scalar.Red, 1);
                    Cv2.ImWrite(Path.Combine(outputFolder, Path.GetFileNameWithoutExtension(fileName) +"_Result" + fileExtension), src);
                }
                return true;
            }
            else
            {
                return false;
            }
        }
        #endregion

    }

    class MouseActions
    {
        public Canvas canvas = new Canvas();
        public System.Windows.Controls.Image dispay = new System.Windows.Controls.Image();

        private Tuple<double, double> Correction()
        {
            double correction_x = (dispay.Width - dispay.ActualWidth) / 2;
            double correction_y = (dispay.Height - dispay.ActualHeight) / 2;
            return Tuple.Create(correction_x, correction_y);
        }

        public void SaveCanvasToImage(string filePath)
        {
            RenderTargetBitmap renderBitmap = new RenderTargetBitmap(
                (int)canvas.ActualWidth, (int)canvas.ActualHeight,
                96d, 96d, PixelFormats.Pbgra32);
            renderBitmap.Render(canvas);
            using (FileStream fileStream = new FileStream(filePath, FileMode.Create))
            {
                PngBitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(renderBitmap));
                encoder.Save(fileStream);
            }
            Console.WriteLine("影像已儲存：" + filePath);
        }

        #region Draw Mask
        public Polyline maskPath { get; set; }
        public bool isDrawing = false;
        public List<List<System.Windows.Point>> maskPaths = new List<List<System.Windows.Point>>();
        public System.Windows.Media.Color color { get; set; }
        public double thickness { get; set; }
        public MouseActions(System.Windows.Media.Color _color, double _thickness)
        {
            color = _color;
            thickness = _thickness;
        }

        private Polyline PolylineAppearance(List<System.Windows.Point> points=null)
        {
            var polyline = new Polyline
            {
                Stroke = new SolidColorBrush(color),
                StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
            };
            if (points?.Count > 0)
            {
                polyline.Points = new PointCollection(points);
            }
            return polyline;
        }

        public void MaskDown(MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                isDrawing = true;
                maskPath = PolylineAppearance();
                canvas.Children.Add(maskPath);
            }
        }

        public void MaskMove(MouseEventArgs e)
        {
            if (isDrawing && maskPath != null)
            {
                System.Windows.Point currentPoint = e.GetPosition(canvas);
                maskPath.Points.Add(currentPoint);
            }
        }

        public void MaskUp()
        {
            if (isDrawing && maskPath != null)
            {
                maskPaths.Add(maskPath.Points.ToList()); // 儲存遮罩點座標
                //List<System.Windows.Point> pointsList = maskPath.Points.ToList();
                //Console.WriteLine("Mask Path Points:");
                //foreach (var point in pointsList)
                //{
                //    Console.WriteLine($"({point.X}, {point.Y})");
                //}
            }
            isDrawing = false;
        }

        private void RemovePolyline()
        {
            var maskElements = canvas.Children.OfType<Polyline>().ToList();
            foreach (var element in maskElements)
            {
                canvas.Children.Remove(element);
            }
        }

        public void MaskMouseRightButtonDown()
        {
            RemovePolyline();
        }

        public void SaveMaskToFile(string filePath)
        {
            var serializableData = maskPaths
                .Select(path => path.Select(p => Tuple.Create(p.X, p.Y)).ToList())
                .ToList();
            string json = JsonConvert.SerializeObject(serializableData, Formatting.Indented);
            File.WriteAllText(filePath, json);
            Console.WriteLine("Mask File 已儲存：" + filePath);
        }

        private void RedrawMasks()
        {
            RemovePolyline();
            foreach (var points in maskPaths)
            {
                Polyline polyline = PolylineAppearance(points);
                canvas.Children.Add(polyline);
            }
        }

        public void LoadMaskFromFile(string filePath)
        {
            if (File.Exists(filePath))
            {
                string json = File.ReadAllText(filePath);
                var serializableData = JsonConvert.DeserializeObject<List<List<Tuple<double, double>>>>(json);
                maskPaths = serializableData
                    .Select(path => path.Select(t => new System.Windows.Point(t.Item1, t.Item2)).ToList())
                    .ToList();
                RedrawMasks();
                Console.WriteLine("Mask File 已導入：" + filePath);
            }
        }

        // Correction
        public void SaveMask(string filePath)
        {
            int width = (int)dispay.ActualWidth;
            int height = (int)dispay.ActualHeight;
            RenderTargetBitmap renderBitmap = new RenderTargetBitmap(width, height, 96d, 96d, PixelFormats.Pbgra32);
            // 建立一個新的視覺物件來繪製 Mask
            double xOffset = Correction().Item1;
            double yOffset = Correction().Item2;
            DrawingVisual dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                // 填充白色背景
                dc.DrawRectangle(System.Windows.Media.Brushes.White, null, new System.Windows.Rect(0, 0, width, height));
                // 繪製 Mask
                foreach (var points in maskPaths)
                {
                    if (points.Count > 1)
                    {
                        // 創建 PathGeometry 來手動繪製 Polyline
                        StreamGeometry geometry = new StreamGeometry();
                        using (StreamGeometryContext ctx = geometry.Open())
                        {
                            var translatedPoints = points.Select(p => new System.Windows.Point(p.X - xOffset, p.Y - yOffset)).ToList();
                            ctx.BeginFigure(translatedPoints[0], false, false); // 起點
                            ctx.PolyLineTo(translatedPoints.Skip(1).ToList(), true, false); // 連接線
                        }
                        geometry.Freeze(); // 提高效能
                        // 使用指定的畫筆繪製
                        System.Windows.Media.Pen maskPen = new System.Windows.Media.Pen(new SolidColorBrush(color), thickness)
                        {
                            StartLineCap = PenLineCap.Round,
                            EndLineCap = PenLineCap.Round,
                            LineJoin = PenLineJoin.Round
                        };
                        dc.DrawGeometry(null, maskPen, geometry);
                    }
                }
            }
            renderBitmap.Render(dv);
            // 儲存影像
            using (FileStream fileStream = new FileStream(filePath, FileMode.Create))
            {
                PngBitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(renderBitmap));
                encoder.Save(fileStream);
            }
            Console.WriteLine("Mask 影像已儲存：" + filePath);
        }
        #endregion

        #region Draw Ruler
        public System.Windows.Point startPoint;
        public Line currentLine;
        public Line startCap, endCap; // 用於端點的小線段
        public List<Line> tickMarks = new List<Line>(); // 刻度線
        public List<TextBlock> tickLabels = new List<TextBlock>(); // 刻度數字
        public List<Line> completedTickMarks = new List<Line>(); // 紀錄刻度線
        public List<TextBlock> completedTickLabels = new List<TextBlock>(); // 紀錄刻度數字
        public double capLength { get; set; } // 端點線長度
        public double tickSpacing { get; set; } // 刻度間距
        public double tickLength { get; set; }   // 刻度線長度
        public MouseActions(double _capLength, double _tickSpacing, double _tickLength)
        {
            capLength = _capLength;
            tickSpacing = _tickSpacing;
            tickLength = _tickLength;
        }

        private void UpdateEndpointCaps(Line cap, System.Windows.Point main, System.Windows.Point other)
        {
            double dx = other.X - main.X;
            double dy = other.Y - main.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length == 0) return; // 避免除以 0
            // 計算垂直方向向量
            double perpX = -dy / length * capLength;
            double perpY = dx / length * capLength;
            cap.X1 = main.X - perpX;
            cap.Y1 = main.Y - perpY;
            cap.X2 = main.X + perpX;
            cap.Y2 = main.Y + perpY;
        }

        private void UpdateTickMarks(System.Windows.Point start, System.Windows.Point end)
        {
            // 只移除當前線段的刻度，不影響舊的刻度
            foreach (var tick in tickMarks)
            {
                canvas.Children.Remove(tick);
            }
            foreach (var label in tickLabels)
            {
                canvas.Children.Remove(label);
            }
            tickMarks.Clear();
            tickLabels.Clear();
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length == 0) return; // 避免除以 0
            double unitX = dx / length;
            double unitY = dy / length;
            double perpX = -unitY * tickLength;
            double perpY = unitX * tickLength;
            // **端點刻度數字** - 起點
            DrawTickMark(start, 0, perpX, perpY);
            // **端點刻度數字** - 終點
            DrawTickMark(end, (int)length, perpX, perpY);
            // **中間刻度**
            for (double d = tickSpacing; d < length; d += tickSpacing)
            {
                double px = start.X + unitX * d;
                double py = start.Y + unitY * d;
                DrawTickMark(new System.Windows.Point(px, py), (int)d, perpX, perpY);
            }
        }

        private void DrawTickMark(System.Windows.Point position, int value, double perpX, double perpY)
        {
            // 創建刻度線
            Line tick = new Line
            {
                Stroke = System.Windows.Media.Brushes.Blue,
                StrokeThickness = 1,
                X1 = position.X - perpX / 2,
                Y1 = position.Y - perpY / 2,
                X2 = position.X + perpX / 2,
                Y2 = position.Y + perpY / 2
            };
            canvas.Children.Add(tick);
            tickMarks.Add(tick);
            // 創建刻度數字
            TextBlock label = new TextBlock
            {
                Text = value.ToString(),
                FontSize = 8,
                Foreground = System.Windows.Media.Brushes.Black
            };
            canvas.Children.Add(label);
            tickLabels.Add(label);
            // 調整文字位置，避免重疊
            Canvas.SetLeft(label, position.X + perpX * 1.5);
            Canvas.SetTop(label, position.Y + perpY * 1.5);
        }

        public void RulerDown(MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                startPoint = e.GetPosition(canvas);
                // 創建新線段
                currentLine = new Line
                {
                    Stroke = System.Windows.Media.Brushes.Black,
                    StrokeThickness = 2,
                    X1 = startPoint.X,
                    Y1 = startPoint.Y,
                    X2 = startPoint.X,
                    Y2 = startPoint.Y
                };
                canvas.Children.Add(currentLine);
                // 創建新端點線
                startCap = new Line { Stroke = System.Windows.Media.Brushes.Red, StrokeThickness = 2 };
                endCap = new Line { Stroke = System.Windows.Media.Brushes.Red, StrokeThickness = 2 };
                canvas.Children.Add(startCap);
                canvas.Children.Add(endCap);
                // **初始化新的 tickMarks 與 tickLabels，舊的紀錄不清除**
                tickMarks = new List<Line>();
                tickLabels = new List<TextBlock>();
            }
        }

        public void RulerMove(MouseEventArgs e)
        {
            if (currentLine != null && e.LeftButton == MouseButtonState.Pressed)
            {
                System.Windows.Point endPoint = e.GetPosition(canvas);
                currentLine.X2 = endPoint.X;
                currentLine.Y2 = endPoint.Y;
                // 更新端點短線
                UpdateEndpointCaps(startCap, startPoint, endPoint);
                UpdateEndpointCaps(endCap, endPoint, startPoint);
                // **只更新當前線段的刻度，不影響舊的**
                UpdateTickMarks(startPoint, endPoint);
            }
        }

        public void RulerUp()
        {
            if (currentLine != null)
            {
                // **將目前線段的刻度與數字移到已完成列表，確保它們不會被清除**
                completedTickMarks.AddRange(tickMarks);
                completedTickLabels.AddRange(tickLabels);
                // 清空當前線段的變數
                currentLine = null;
            }
        }

        public void RulerMouseRightButtonDown()
        {
            var maskElements = canvas.Children.OfType<UIElement>()
            .Where(e => e is Line || e is TextBlock)
            .ToList();
            maskElements.ForEach(e => canvas.Children.Remove(e));
        }
        #endregion

        #region Draw Rect
        public bool _started;
        public System.Windows.Point _startPoint;
        public System.Windows.Point _endPoint;
        public MouseActions()
        {
        }

        // Correction
        private void ShowRect(System.Windows.Shapes.Rectangle rectangle, System.Windows.Point point)
        {
            var rect = new System.Windows.Rect(_startPoint, point);
            rectangle.Margin = new Thickness(rect.Left + Correction().Item1, rect.Top + Correction().Item2, 0, 0);
            rectangle.Width = rect.Width;
            rectangle.Height = rect.Height;
        }

        public void RectDown(MouseButtonEventArgs e)
        {
            _started = true;
            _startPoint = e.GetPosition(dispay);
            //Console.WriteLine($"X座標:{e.GetPosition(dispay).X}");
            //Console.WriteLine($"Y座標:{e.GetPosition(dispay).Y}");
        }

        public void RectMove(System.Windows.Shapes.Rectangle rectangle, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                if (_started)
                {
                    _endPoint = e.GetPosition(dispay);
                    ShowRect(rectangle, _endPoint);
                }
            }
        }

        public void RectUp()
        {
            _started = false;
        }

        private System.Windows.Point ConvertCoord(System.Windows.Point _startPoint)
        {
            return new System.Windows.Point(_startPoint.X * 1920 / dispay.ActualWidth, _startPoint.Y * 1080 / dispay.ActualHeight);
        }
        #endregion

        #region Draw Circle
        public System.Windows.Shapes.Ellipse ellipse;
        public bool CircleisDrawing = false;
        public System.Windows.Point CirclestartPoint;

        public  void CircleDown(MouseButtonEventArgs e)
        {
            if (!CircleisDrawing)
            {
                CircleisDrawing = true;
                CirclestartPoint = e.GetPosition(canvas);
                // 創建圓形元素
                ellipse = new Ellipse
                {
                    Stroke = System.Windows.Media.Brushes.Red,
                    StrokeThickness = 2
                };
                // 添加圓形到畫布
                canvas.Children.Add(ellipse);
            }
        }

        public void CircleMove(MouseEventArgs e)
        {
            if (ellipse == null || !CircleisDrawing) return;
            System.Windows.Point currentPoint = e.GetPosition(canvas);
            // 計算圓心與滑鼠位置的距離作為半徑
            double radius = Math.Sqrt(Math.Pow(currentPoint.X - CirclestartPoint.X, 2) + Math.Pow(currentPoint.Y - CirclestartPoint.Y, 2));
            // 設置圓形的大小和位置
            ellipse.Width = radius * 2;
            ellipse.Height = radius * 2;
            Canvas.SetLeft(ellipse, CirclestartPoint.X - radius);
            Canvas.SetTop(ellipse, CirclestartPoint.Y - radius);
        }

        public void CircleUp()
        {
            CircleisDrawing = false;
        }
        #endregion

        #region Draw Arrow
        private System.Windows.Shapes.Line blade;
        private Polygon swordHead;
        private bool ArrowisDrawing = false;

        public void ArrowDown(MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                startPoint = e.GetPosition(canvas);
                ArrowisDrawing = true;
                // 劍刃 (Line)
                blade = new System.Windows.Shapes.Line
                {
                    Stroke = System.Windows.Media.Brushes.Red,
                    StrokeThickness = 2
                };
                canvas.Children.Add(blade);
                // 劍頭 (三角形箭頭)
                swordHead = new Polygon
                {
                    Fill = System.Windows.Media.Brushes.Red,
                    Stroke = System.Windows.Media.Brushes.Red,
                    StrokeThickness = 1
                };
                canvas.Children.Add(swordHead);
            }
        }

        public void ArrowMove(MouseEventArgs e)
        {
            if (ArrowisDrawing)
            {
                System.Windows.Point endPoint = e.GetPosition(canvas);
                // 更新劍刃 (劍身)
                blade.X1 = startPoint.X;
                blade.Y1 = startPoint.Y;
                blade.X2 = endPoint.X;
                blade.Y2 = endPoint.Y;
                // 計算劍頭方向
                Vector direction = new Vector(endPoint.X - startPoint.X, endPoint.Y - startPoint.Y);
                direction.Normalize();
                Vector normal = new Vector(-direction.Y, direction.X);
                // 劍頭大小
                double swordHeadSize = 5;
                System.Windows.Point tip = endPoint;
                System.Windows.Point left = endPoint - direction * swordHeadSize + normal * swordHeadSize / 2;
                System.Windows.Point right = endPoint - direction * swordHeadSize - normal * swordHeadSize / 2;
                // 更新劍頭 (三角形)
                swordHead.Points.Clear();
                swordHead.Points.Add(tip);
                swordHead.Points.Add(left);
                swordHead.Points.Add(right);
            }
        }

        public void ArrowUp()
        {
            ArrowisDrawing = false; // 完成繪製
        }
        #endregion
    }

}
