using System;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace AudioRecorderTrayApp
{
    public partial class NotificationWindow : Window
    {
        private readonly DispatcherTimer _closeTimer;
        public NotificationWindow(string message)
        {
            InitializeComponent();
            MessageTextBlock.Text = message;
            _closeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(4)
            };
            _closeTimer.Tick += (sender, args) => CloseAnimation();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var desktopWorkingArea = SystemParameters.WorkArea;
            this.Left = desktopWorkingArea.Right - this.Width;
            this.Top = desktopWorkingArea.Bottom - this.Height;
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.5)));
            _closeTimer.Start();
        }

        private void CloseAnimation()
        {
            _closeTimer.Stop();
            var anim = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.5));
            anim.Completed += (s, _) => this.Close();
            this.BeginAnimation(OpacityProperty, anim);
        }
    }
}
