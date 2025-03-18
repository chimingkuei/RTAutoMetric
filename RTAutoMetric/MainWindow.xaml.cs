using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using static System.Net.Mime.MediaTypeNames;
using static RTAutoMetric.BaseLogRecord;
using System.Windows.Media.Media3D;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Controls.Primitives;
using netDxf.Entities;
using Xceed.Wpf.Toolkit;
using Microsoft.Win32;
using System.Windows.Interop;


namespace RTAutoMetric
{
    #region Config Class
    public class SerialNumber
    {
        [JsonProperty("MaskThickness_val")]
        public string MaskThickness_val { get; set; }
        [JsonProperty("colorPicker_val")]
        public System.Windows.Media.Color colorPicker_val { get; set; }
    }

    public class Model
    {
        [JsonProperty("SerialNumbers")]
        public SerialNumber SerialNumbers { get; set; }
    }

    public class RootObject
    {
        [JsonProperty("Models")]
        public List<Model> Models { get; set; }
    }
    #endregion

    public partial class MainWindow : System.Windows.Window
    {
        
        public MainWindow()
        {
            InitializeComponent();
        }

        #region Function
        private void WindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (System.Windows.MessageBox.Show("請問是否要關閉？", "確認", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                e.Cancel = false;
            }
            else
            {
                e.Cancel = true;
            }
        }

        #region Config
        private SerialNumber SerialNumberClass()
        {
            SerialNumber serialnumber_ = new SerialNumber
            {
                MaskThickness_val = MaskThickness.Text,
                colorPicker_val = (System.Windows.Media.Color)colorPicker.SelectedColor
            };
            return serialnumber_;
        }

        private void LoadConfig(int model, int serialnumber, bool encryption = false)
        {
            List<RootObject> Parameter_info = Config.Load(encryption);
            if (Parameter_info != null)
            {
                MaskThickness.Text = Parameter_info[model].Models[serialnumber].SerialNumbers.MaskThickness_val;
                colorPicker.SelectedColor = Parameter_info[model].Models[serialnumber].SerialNumbers.colorPicker_val;
            }
            else
            {
                // 結構:2個Models、Models下在各2個SerialNumbers
                SerialNumber serialnumber_ = SerialNumberClass();
                List<Model> models = new List<Model>
                {
                    new Model { SerialNumbers = serialnumber_ },
                    new Model { SerialNumbers = serialnumber_ }
                };
                List<RootObject> rootObjects = new List<RootObject>
                {
                    new RootObject { Models = models },
                    new RootObject { Models = models }
                };
                Config.SaveInit(rootObjects, encryption);
            }
        }
       
        private void SaveConfig(int model, int serialnumber, bool encryption = false)
        {
            Config.Save(model, serialnumber, SerialNumberClass(), encryption);
        }
        #endregion

        #region Dispatcher Invoke 
        public string DispatcherGetValue(TextBox control)
        {
            string content = "";
            this.Dispatcher.Invoke(() =>
            {
                content = control.Text;
            });
            return content;
        }

        public void DispatcherSetValue(string content, TextBox control)
        {
            this.Dispatcher.Invoke(() =>
            {
                control.Text = content;
            });
        }
        #endregion

        private void ParaInit()
        {
            Rectangle.Visibility = Visibility.Collapsed;
            Mag.IsEnabled = false;
            if (!File.Exists(@"Config.json"))
            {
                mask = new MouseActions(System.Windows.Media.Color.FromArgb(120, 0, 0, 0), 10);
            }
            else
            {
                var color = System.Windows.Media.Color.FromArgb(120, colorPicker.SelectedColor.Value.R, colorPicker.SelectedColor.Value.G, colorPicker.SelectedColor.Value.B);
                mask = new MouseActions(color, Convert.ToInt32(MaskThickness.Text));
            }
            mask.canvas = myCanvas;
            ruler.canvas = myCanvas;
            mask.dispay = Display_Screen;
            ruler.dispay = Display_Screen;
            rect.dispay = Display_Screen;
        }

        #region MouseActions
        private void Image_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if ((bool)RectOnOff.IsChecked)
            {
                rect.RectDown(e);
            }
        }

        private void Image_MouseMove(object sender, MouseEventArgs e)
        {
            if ((bool)RectOnOff.IsChecked)
            {
                rect.RectMove(Rectangle, e);
            }
        }

        private void Image_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if ((bool)RectOnOff.IsChecked)
            {
                rect.RectUp();
            }
        }

        private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if ((bool)MaskOnOff.IsChecked)
                mask.MaskDown(e);
            if ((bool)RulerOnOff.IsChecked)
                ruler.RulerDown(e);
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if ((bool)MaskOnOff.IsChecked)
                mask.MaskMove(e);
            if ((bool)RulerOnOff.IsChecked)
                ruler.RulerMove(e);
        }

        private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if ((bool)MaskOnOff.IsChecked)
                mask.MaskUp();
            if ((bool)RulerOnOff.IsChecked)
                ruler.RulerUp();
        }

        private void Canvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if ((bool)MaskOnOff.IsChecked)
                mask.MaskMouseRightButtonDown();
            if ((bool)RulerOnOff.IsChecked)
                ruler.RulerMouseRightButtonDown();
        }
        #endregion
        #endregion

        #region Parameter and Init
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LoadConfig(0, 0);
            ParaInit();
        }
        BaseConfig<RootObject> Config = new BaseConfig<RootObject>();
        Core Do = new Core();
        public bool _started;
        public System.Windows.Point _startPoint;
        public System.Windows.Point _endPoint;
        CancellationTokenSource cts;
        MouseActions mask;
        MouseActions ruler = new MouseActions(10, 20, 5);
        MouseActions rect = new MouseActions();
        bool SelectedState = false;
        #region Log
        BaseLogRecord Logger = new BaseLogRecord();
        //Logger.WriteLog("儲存參數!", LogLevel.General, richTextBoxGeneral);
        #endregion
        #endregion

        #region Main Screen
        private void Main_Btn_Click(object sender, RoutedEventArgs e)
        {
            switch ((sender as Button).Name)
            {
                case nameof(Capture_Screen):
                    {
                        BitmapImage regionImage = Do.CaptureRegion<BitmapImage>(0, 0, 1920, 1080);
                        Display_Screen.Source = regionImage;
                        break;
                    }
                case nameof(Save_Mask):
                    {
                        if ((bool)MaskOnOff.IsChecked)
                            mask.SaveMask(@"Mask.png");
                        break;
                    }
                case nameof(Save_MaskFile):
                    {
                        if ((bool)MaskOnOff.IsChecked)
                            mask.SaveMaskToFile(@"MaskFile.json");
                        break;
                    }
                case nameof(Load_MaskFile):
                    {
                        if ((bool)MaskOnOff.IsChecked)
                            mask.LoadMaskFromFile(@"MaskFile.json");
                        break;
                    }
                case nameof(Save_Canvas):
                    {
                        mask.SaveCanvasToImage(@"Canvas.png");
                        break;
                    }
                case nameof(Save_Config):
                    {
                        SaveConfig(0, 0);
                        break;
                    }
            }
        }

        private void Main_CheckBox_Click(object sender, RoutedEventArgs e)
        {
            switch ((sender as CheckBox).Name)
            {
                case nameof(RectOnOff):
                    {
                        Rectangle.Visibility = RectOnOff.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
                        break;
                    }
                case nameof(MagOnOff):
                    {
                        Mag.IsEnabled = MagOnOff.IsChecked == true;
                        break;
                    }
            }
        }
        #endregion

        #region Parameter Screen
        private void Parameter_Btn_Click(object sender, RoutedEventArgs e)
        {
            switch ((sender as Button).Name)
            {
                case nameof(Open_ImagePath):
                    {
                        OpenFileDialog openFileDialog = new OpenFileDialog();
                        openFileDialog.Filter = "Image files|*.bmp;*.jpg;*.png;*|All files|*.*";
                        if (openFileDialog.ShowDialog() == true)
                        {
                            BitmapImage bitmapImage = new BitmapImage(new Uri(openFileDialog.FileName));
                            Display_Screen.Source = bitmapImage;
                            ImagePath.Text = openFileDialog.FileName;
                        }
                        break;
                    }
            }
        }

        private void ColorPicker_SelectedColorChanged(object sender, RoutedPropertyChangedEventArgs<System.Windows.Media.Color?> e)
        {
            if (e.NewValue.HasValue)
            {
                var selectedColor = e.NewValue.Value;
                var convertedColor = System.Windows.Media.Color.FromArgb(120, selectedColor.R, selectedColor.G, selectedColor.B);
                SelectedState = !SelectedState ? true : (mask.color = convertedColor) == convertedColor;
            }
        }
        #endregion
    }
}
