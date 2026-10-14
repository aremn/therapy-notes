using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using AudioRecorderTrayApp.Services;

namespace AudioRecorderTrayApp
{
    public partial class DialogueViewerWindow : Window
    {
        public DialogueViewerWindow()
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

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Load_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var id = IdBox.Text?.Trim();
                if (string.IsNullOrWhiteSpace(id))
                {
                    OutputBox.Text = "Введите session id";
                    return;
                }

                var text = DialogueStore.Load(id);
                if (text == null)
                {
                    OutputBox.Text = "Файл с диалогом не найден";
                }
                else
                {
                    var cleanText = DialogueFormatter.RemoveLineNumbers(text);
                    if (ApplyMapCheck.IsChecked == true)
                    {
                        var map = MappingStore.Load(id);
                        if (map != null)
                        {
                            OutputBox.Text = ApplyReverseMapping(cleanText, map);
                            MapStatus.Text = $"mapping: {map.Count}";
                        }
                        else
                        {
                            OutputBox.Text = cleanText;
                            MapStatus.Text = "mapping: нет";
                        }
                    }
                    else
                    {
                        OutputBox.Text = cleanText;
                        MapStatus.Text = string.Empty;
                    }
                }
            }
            catch (Exception ex)
            {
                OutputBox.Text = ex.Message;
            }
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dir = DialogueStore.RootPath;
                Directory.CreateDirectory(dir);
                Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
            }
            catch
            {
            }
        }

        private void ApplyMapCheck_Changed(object sender, RoutedEventArgs e)
        {
            Load_Click(sender, e);
        }

        private static string ApplyReverseMapping(string text, System.Collections.Generic.Dictionary<string, string> aliasToOriginal)
        {
            if (string.IsNullOrEmpty(text) || aliasToOriginal == null || aliasToOriginal.Count == 0)
                return text;
            var ordered = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>>(aliasToOriginal);
            ordered.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));
            var result = text;
            foreach (var kv in ordered)
            {
                var alias = kv.Key;
                var original = kv.Value ?? string.Empty;
                result = result.Replace("[" + alias + "]", original);
            }

            return result;
        }
    }
}
