using System.Windows;

namespace AudioRecorderTrayApp
{
    public partial class DownloadWindow : Window
    {
        public DownloadWindow()
        {
            InitializeComponent();
            ModelManager.OnDownloadProgress += UpdateProgress;
        }

        private void UpdateProgress(double percentage)
        {
            Dispatcher.Invoke(() =>
            {
                DownloadProgressBar.Value = percentage;
                if (percentage >= 100)
                {
                    this.Close();
                }
            });
        }

        protected override void OnClosed(EventArgs e)
        {
            ModelManager.OnDownloadProgress -= UpdateProgress;
            base.OnClosed(e);
        }
    }
}
