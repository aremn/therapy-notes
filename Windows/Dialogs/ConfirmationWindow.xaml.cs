namespace AudioRecorderTrayApp
{
    using System.ComponentModel;
    using System.Windows;

    public partial class ConfirmationWindow : Window
    {
        public double WindowLeft { get; private set; }
        public double WindowTop { get; private set; }

        public ConfirmationWindow()
        {
            InitializeComponent();
            this.Closing += OnWindowClosing;
            this.Loaded += OnWindowLoaded;
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            this.Left = (screenWidth / 2) - (this.ActualWidth / 2);
            this.Top = 20;
        }

        private void OnWindowClosing(object? sender, CancelEventArgs e)
        {
            this.WindowLeft = this.Left;
            this.WindowTop = this.Top;
        }

        private void YesButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
            this.Close();
        }

        private void NoButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}
