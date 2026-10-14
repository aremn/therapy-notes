using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace AudioRecorderTrayApp
{
    public partial class PermissionDialog : Window
    {
        public PermissionDialog(string mainMessage, string systemError)
        {
            InitializeComponent();
            ErrorMessageText.Text = mainMessage;
            if (string.IsNullOrEmpty(systemError))
            {
                SystemErrorText.Visibility = Visibility.Collapsed;
            }
            else
            {
                SystemErrorText.Text = $"Системная ошибка: \"{systemError}\"";
            }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        private void CustomCloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void OpenSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = "ms-settings:privacy-microphone", UseShellExecute = true });
            }
            catch
            {
                System.Windows.MessageBox.Show("Не удалось автоматически открыть настройки. Пожалуйста, откройте их вручную: Параметры -> Конфиденциальность и защита -> Микрофон.");
            }

            this.Close();
        }
    }
}
