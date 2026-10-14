using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Windows;
using AudioRecorderTrayApp.Services;

namespace AudioRecorderTrayApp
{
    public partial class MappingViewerWindow : Window
    {
        public MappingViewerWindow()
        {
            InitializeComponent();
        }

        private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
            {
                try
                {
                    DragMove();
                }
                catch
                {
                }
            }
        }

        private void OnLoadClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var id = IdBox.Text?.Trim();
                if (string.IsNullOrWhiteSpace(id))
                {
                    OutputBox.Text = "Введите session id";
                    return;
                }

                var map = MappingStore.Load(id);
                if (map == null)
                {
                    OutputBox.Text = "Файл не найден или не удалось расшифровать.";
                    return;
                }

                OutputBox.Text = JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            }
            catch (Exception ex)
            {
                OutputBox.Text = $"Ошибка: {ex.Message}";
            }
        }

        private void OnOpenFolderClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var dir = MappingStore.RootPath;
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
            }
            catch
            {
            }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
