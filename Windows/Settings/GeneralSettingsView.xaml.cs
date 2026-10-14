using System.Windows.Controls;
using System.Windows;
using NAudio.CoreAudioApi;
using Microsoft.Win32;
using System.Reflection;

namespace AudioRecorderTrayApp
{
    public partial class GeneralSettingsView : System.Windows.Controls.UserControl
    {
        private readonly Dictionary<string, string> _microphoneDevices = new Dictionary<string, string>();
        public GeneralSettingsView()
        {
            InitializeComponent();
            LoadMicrophones();
            LoadAndSetCurrentTheme();
            LoadAndSetVolumes();
            try
            {
                StartupToggleButton.IsChecked = IsStartupEnabled();
            }
            catch
            {
            }

            try
            {
                AutoRecordToggle.IsChecked = AppSettings.Current.AutoRecordOnCall;
            }
            catch
            {
            }
        }

        private void LoadAndSetCurrentTheme()
        {
            var savedThemeName = AppSettings.Current.Theme;
            var itemToSelect = ThemeComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(x => x.Content.ToString() == savedThemeName);
            if (itemToSelect != null)
            {
                ThemeComboBox.SelectedItem = itemToSelect;
            }
            else
            {
                ThemeComboBox.SelectedIndex = 2;
            }
        }

        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ThemeComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                var themeName = selectedItem.Content.ToString();
                Theme theme = themeName switch
                {
                    "Темная" => Theme.Dark,
                    "Системная" => Theme.System,
                    _ => Theme.Light,
                };
                ThemeManager.ApplyTheme(theme);
                AppSettings.Current.Theme = themeName!;
                AppSettings.Save();
            }
        }

        private void LoadAndSetVolumes()
        {
            MicVolumeSlider.Value = AppSettings.Current.MicrophoneVolume * 100;
            SystemVolumeSlider.Value = AppSettings.Current.SystemVolume * 100;
            MicVolumeLabel.Text = $"{MicVolumeSlider.Value:F0}%";
            SystemVolumeLabel.Text = $"{SystemVolumeSlider.Value:F0}%";
        }

        private void MicVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (MicVolumeLabel != null)
            {
                MicVolumeLabel.Text = $"{e.NewValue:F0}%";
                AppSettings.Current.MicrophoneVolume = e.NewValue / 100.0;
                AppSettings.Save();
            }
        }

        private void SystemVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (SystemVolumeLabel != null)
            {
                SystemVolumeLabel.Text = $"{e.NewValue:F0}%";
                AppSettings.Current.SystemVolume = e.NewValue / 100.0;
                AppSettings.Save();
            }
        }

        private void LoadMicrophones()
        {
            MicrophoneComboBox.Items.Clear();
            _microphoneDevices.Clear();
            var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            foreach (var device in devices)
            {
                if (!_microphoneDevices.ContainsKey(device.FriendlyName))
                {
                    _microphoneDevices.Add(device.FriendlyName, device.ID);
                    MicrophoneComboBox.Items.Add(device.FriendlyName);
                }
            }

            var savedId = AppSettings.Current.SelectedMicrophoneId;
            if (!string.IsNullOrEmpty(savedId) && _microphoneDevices.ContainsValue(savedId))
            {
                var kv = _microphoneDevices.FirstOrDefault(x => x.Value == savedId);
                if (kv.Key != null)
                {
                    MicrophoneComboBox.SelectedItem = kv.Key;
                }
            }
            else if (MicrophoneComboBox.Items.Count > 0)
            {
                MicrophoneComboBox.SelectedIndex = 0;
            }
        }

        private void MicrophoneComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MicrophoneComboBox.SelectedItem is string selectedName)
            {
                if (_microphoneDevices.TryGetValue(selectedName, out var deviceId))
                {
                    AppSettings.Current.SelectedMicrophoneId = deviceId;
                    AppSettings.Save();
                }
            }
        }

        private void StartupToggleButton_CheckedChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                bool enable = StartupToggleButton.IsChecked == true;
                SetStartup(enable);
            }
            catch
            {
            }
        }

        private void AutoRecordToggle_Changed(object sender, RoutedEventArgs e)
        {
            AppSettings.Current.AutoRecordOnCall = AutoRecordToggle.IsChecked == true;
            AppSettings.Save();
            try
            {
                var main = System.Windows.Application.Current?.Windows?.OfType<MainWindow>().FirstOrDefault();
                main?.ApplyAutoRecordSetting();
            }
            catch
            {
            }
        }

        private static bool IsStartupEnabled()
        {
            const string runKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
            var appName = GetAppRunName();
            using var key = Registry.CurrentUser.OpenSubKey(runKey, writable: false);
            var val = key?.GetValue(appName) as string;
            return !string.IsNullOrEmpty(val);
        }

        private static void SetStartup(bool enable)
        {
            const string runKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
            var appName = GetAppRunName();
            using var key = Registry.CurrentUser.OpenSubKey(runKey, writable: true) ?? Registry.CurrentUser.CreateSubKey(runKey, writable: true);
            if (enable)
            {
                var exePath = GetExePath();
                key.SetValue(appName, '"' + exePath + '"');
            }
            else
            {
                try
                {
                    key.DeleteValue(appName, throwOnMissingValue: false);
                }
                catch
                {
                }
            }
        }

        private static string GetExePath()
        {
            try
            {
                return System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName!;
            }
            catch
            {
                return Assembly.GetEntryAssembly()!.Location;
            }
        }

        private static string GetAppRunName()
        {
            return "AudioRecorderTrayApp";
        }
    }
}
