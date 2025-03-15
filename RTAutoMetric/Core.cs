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
        public Polyline maskPath;
        public bool isDrawing = false;
        public List<List<System.Windows.Point>> maskPaths = new List<List<System.Windows.Point>>();
        public Canvas canves = new Canvas();
        public System.Windows.Media.Color color = System.Windows.Media.Color.FromArgb(120, 0, 0, 0);
        public double thickness = 10;

        private Polyline PolylineAppearance(List<System.Windows.Point> points=null)
        {
            var polyline = new Polyline
            {
                Stroke = new SolidColorBrush(color),
                StrokeThickness = thickness,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
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
                canves.Children.Add(maskPath);
            }
        }

        public void MaskMove(MouseEventArgs e)
        {
            if (isDrawing && maskPath != null)
            {
                System.Windows.Point currentPoint = e.GetPosition(canves);
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
            var maskElements = canves.Children.OfType<Polyline>().ToList();
            foreach (var element in maskElements)
            {
                canves.Children.Remove(element);
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
                canves.Children.Add(polyline);
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

        public void SaveCanvasToImage(string filePath)
        {
            RenderTargetBitmap renderBitmap = new RenderTargetBitmap(
                (int)canves.ActualWidth, (int)canves.ActualHeight,
                96d, 96d, PixelFormats.Pbgra32);
            renderBitmap.Render(canves);
            using (FileStream fileStream = new FileStream(filePath, FileMode.Create))
            {
                PngBitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(renderBitmap));
                encoder.Save(fileStream);
            }
            Console.WriteLine("影像已儲存：" + filePath);
        }


    }

}
