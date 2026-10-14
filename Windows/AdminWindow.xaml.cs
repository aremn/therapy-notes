using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using NAudio.Wave;
using System.Windows.Controls;
using AudioRecorderTrayApp.Services;
using AudioRecorderTrayApp.Models;
using AudioRecorderTrayApp.Core;

namespace AudioRecorderTrayApp
{
    public partial class AdminWindow : Window
    {
        private string? _currentSessionId;
        private System.Windows.Controls.ProgressBar? GetProgressBar() => this.FindName("ProgressBar") as System.Windows.Controls.ProgressBar;
        private System.Windows.Controls.TextBlock? GetStatusText() => this.FindName("StatusText") as System.Windows.Controls.TextBlock;
        private System.Windows.Controls.TextBox? GetFilePathBox() => this.FindName("FilePathBox") as System.Windows.Controls.TextBox;
        private System.Windows.Controls.ComboBox? GetSpeakerCombo() => this.FindName("SpeakerCombo") as System.Windows.Controls.ComboBox;
        private System.Windows.Controls.Button? GetTranscribeButton() => this.FindName("TranscribeButton") as System.Windows.Controls.Button;
        private void ShowProgress(bool visible)
        {
            try
            {
                var pb = GetProgressBar();
                if (pb != null)
                    pb.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }
            catch
            {
            }
        }

        private void SetProgressPercent(double pct)
        {
            try
            {
                var pb = GetProgressBar();
                if (pb != null)
                {
                    pb.Minimum = 0;
                    pb.Maximum = 100;
                    pb.Value = Math.Max(0, Math.Min(100, pct));
                    pb.Visibility = Visibility.Visible;
                }
            }
            catch
            {
            }
        }

        private void SetStatus(string text)
        {
            try
            {
                var st = GetStatusText();
                if (st != null)
                    st.Text = text;
            }
            catch
            {
            }
        }

        private void AppendStatus(string text)
        {
            try
            {
                var st = GetStatusText();
                if (st != null)
                    st.Text += text;
            }
            catch
            {
            }
        }

        public AdminWindow()
        {
            InitializeComponent();
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
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

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
        }

        private void PickFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Audio files (*.wav;*.mp3;*.m4a)|*.wav;*.mp3;*.m4a|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog() == true)
            {
                var tb = GetFilePathBox();
                if (tb != null)
                    tb.Text = dlg.FileName;
            }
        }

        private async void Transcribe_Click(object sender, RoutedEventArgs e)
        {
            var tbPath = GetFilePathBox();
            var path = tbPath?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                SetStatus("Выберите существующий файл");
                return;
            }

            string speaker = "client";
            try
            {
                var sc = GetSpeakerCombo();
                if (sc != null && sc.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Content is string s && !string.IsNullOrWhiteSpace(s))
                    speaker = s;
            }
            catch
            {
            }

            var btn = GetTranscribeButton();
            if (btn != null)
                btn.IsEnabled = false;
            ShowProgress(true);
            SetStatus("Запуск пайплайна оффлайн...");
            try
            {
                await Task.Yield();
                string dummySilentPath = CreateSilentWav(TimeSpan.FromSeconds(1));
                string therapistWav;
                string clientWav;
                if (string.Equals(speaker, "therapist", StringComparison.OrdinalIgnoreCase))
                {
                    therapistWav = path;
                    clientWav = dummySilentPath;
                }
                else
                {
                    therapistWav = dummySilentPath;
                    clientWav = path;
                }

                var result = await UploadOrchestrator.ProcessAndUploadAsync(therapistWav, clientWav);
                _currentSessionId = result.Id;
                SetStatus($"Готово: сессия {result.Id}, сегментов={result.Segments.Count}, длит.={result.DurationSeconds}с");
                try
                {
                    System.Windows.Clipboard.SetText(result.Id ?? string.Empty);
                }
                catch
                {
                }

                new NotificationWindow($"Сессия сохранена. ID скопирован: {result.Id}").Show();
                AppendStatus($" | session id: {result.Id}");
                try
                {
                    if (File.Exists(dummySilentPath))
                        File.Delete(dummySilentPath);
                }
                catch
                {
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Ошибка: {ex.Message}");
            }
            finally
            {
                if (btn != null)
                    btn.IsEnabled = true;
                ShowProgress(false);
            }
        }

        private async void UploadTranscript_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog() != true)
            {
                return;
            }

            var path = dlg.FileName;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                SetStatus("Выберите существующий файл");
                return;
            }

            var btn = sender as System.Windows.Controls.Button;
            if (btn != null)
                btn.IsEnabled = false;
            ShowProgress(true);
            SetStatus("Запуск пайплайна для текста...");
            try
            {
                string rawText = await File.ReadAllTextAsync(path);
                if (string.IsNullOrWhiteSpace(rawText))
                {
                    SetStatus("Файл пуст.");
                    return;
                }

                var result = await UploadOrchestrator.ProcessAndUploadTextAsync(rawText);
                _currentSessionId = result.Id;
                SetStatus($"Готово: сессия {result.Id}, строк={result.Segments.Count}, ~{result.DurationSeconds}с");
                try
                {
                    System.Windows.Clipboard.SetText(result.Id ?? string.Empty);
                }
                catch
                {
                }

                new NotificationWindow($"Сессия из текста сохранена. ID скопирован: {result.Id}").Show();
                AppendStatus($" | session id: {result.Id}");
            }
            catch (Exception ex)
            {
                SetStatus($"Ошибка: {ex.Message}");
            }
            finally
            {
                if (btn != null)
                    btn.IsEnabled = true;
                ShowProgress(false);
            }
        }

        private static string CreateSilentWav(TimeSpan duration)
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"silent_{Guid.NewGuid():N}.wav");
            var format = new WaveFormat(16000, 16, 1);
            using (var writer = new WaveFileWriter(path, format))
            {
                int totalSamples = (int)Math.Max(0, duration.TotalSeconds * format.SampleRate);
                int totalBytes = totalSamples * format.BlockAlign;
                var zero = new byte[8192];
                int remaining = totalBytes;
                while (remaining > 0)
                {
                    int toWrite = Math.Min(remaining, zero.Length);
                    writer.Write(zero, 0, toWrite);
                    remaining -= toWrite;
                }

                writer.Flush();
            }

            return path;
        }

        private void OpenMappingViewer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                new MappingViewerWindow().Show();
            }
            catch
            {
            }
        }

        private void OpenDialogueViewer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                new DialogueViewerWindow().Show();
            }
            catch
            {
            }
        }
    }
}
