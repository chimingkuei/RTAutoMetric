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


namespace RTAutoMetric
{
    class Core
    {
        TesseractEngine ocr;

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
