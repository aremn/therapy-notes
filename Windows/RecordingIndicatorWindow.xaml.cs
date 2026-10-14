using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AudioRecorderTrayApp
{
    public partial class RecordingIndicatorWindow : Window
    {
        private readonly System.Windows.Threading.DispatcherTimer _timer;
        private readonly DateTime _startTime;
        private readonly MainWindow _mainWindow;
        private const double CollapsedWidth = 40;
        private const double ExpandedWidth = 140;
        public RecordingIndicatorWindow(MainWindow mainWindow)
        {
            InitializeComponent();
            _mainWindow = mainWindow;
            _startTime = DateTime.Now;
            _timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            SetInitialPosition();
            await AnimateOnStartup();
        }

        private async Task AnimateOnStartup()
        {
            this.Opacity = 0;
            AnimationHost.Width = ExpandedWidth;
            TextPanel.Opacity = 1;
            this.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromSeconds(0.4)));
            await Task.Delay(400);
            await Task.Delay(2500);
            if (!AnimationHost.IsMouseOver)
            {
                var collapseAnimation = new DoubleAnimation
                {
                    To = CollapsedWidth,
                    Duration = TimeSpan.FromSeconds(0.3),
                    EasingFunction = (ExponentialEase)this.Resources["ExpoEase"]
                };
                collapseAnimation.Completed += (s, e) =>
                {
                    AnimationHost.BeginAnimation(WidthProperty, null);
                    AnimationHost.Width = CollapsedWidth;
                };
                AnimationHost.BeginAnimation(WidthProperty, collapseAnimation);
            }
        }

        private void SetInitialPosition()
        {
            if (AppSettings.Current.IndicatorPosition.HasValue)
            {
                this.Left = AppSettings.Current.IndicatorPosition.Value.X;
                this.Top = AppSettings.Current.IndicatorPosition.Value.Y;
            }
            else
            {
                var screen = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position);
                var area = screen.WorkingArea;
                var source = PresentationSource.FromVisual(this);
                if (source?.CompositionTarget != null)
                {
                    var dpiX = source.CompositionTarget.TransformToDevice.M11;
                    var dpiY = source.CompositionTarget.TransformToDevice.M22;
                    var screenLeft = area.Left / dpiX;
                    var screenTop = area.Top / dpiY;
                    var screenWidth = area.Width / dpiX;
                    this.Left = screenLeft + (screenWidth - ExpandedWidth) / 2.0;
                    this.Top = screenTop + 20;
                }
            }
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            var elapsed = DateTime.Now - _startTime;
            var totalHours = (int)elapsed.TotalHours;
            TimerTextBlock.Text = string.Format("{0:D2}:{1:D2}:{2:D2}", totalHours, elapsed.Minutes, elapsed.Seconds);
        }

        private void MainBorder_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed && e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
                AppSettings.Current.IndicatorPosition = new System.Windows.Point(this.Left, this.Top);
                AppSettings.Save();
            }
        }

        private async void StopButton_Click(object sender, RoutedEventArgs e)
        {
            StopButton.IsEnabled = false;
            await _mainWindow.ToggleRecordingAsync();
        }

        public void StopTimerAndClose()
        {
            _timer.Stop();
            var fadeOut = new DoubleAnimation(0, TimeSpan.FromSeconds(0.3));
            fadeOut.Completed += (s, e) => this.Close();
            this.BeginAnimation(OpacityProperty, fadeOut);
        }
    }
}
