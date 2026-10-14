using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NAudio.Wave;

namespace AudioRecorderTrayApp
{
    public partial class HotkeysSettingsView : System.Windows.Controls.UserControl
    {
        private HotkeySettings _tempHotkey;
        public HotkeysSettingsView()
        {
            InitializeComponent();
            _tempHotkey = AppSettings.Current.Hotkey;
            UpdateTextBox();
        }

        private void HotkeyTextBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            e.Handled = true;
            var modifiers = Keyboard.Modifiers;
            var key = e.Key;
            if (key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt || key == Key.LeftShift || key == Key.RightShift || key == Key.System || key == Key.DeadCharProcessed)
            {
                return;
            }

            _tempHotkey = new HotkeySettings
            {
                Modifiers = modifiers,
                Key = key
            };
            UpdateTextBox();
            AppSettings.SaveAndNotifyHotkey(_tempHotkey);
        }

        private void UpdateTextBox()
        {
            string hotkeyText = "";
            if (_tempHotkey.Modifiers.HasFlag(ModifierKeys.Control))
                hotkeyText += "Ctrl + ";
            if (_tempHotkey.Modifiers.HasFlag(ModifierKeys.Shift))
                hotkeyText += "Shift + ";
            if (_tempHotkey.Modifiers.HasFlag(ModifierKeys.Alt))
                hotkeyText += "Alt + ";
            hotkeyText += _tempHotkey.Key.ToString();
            HotkeyTextBox.Text = hotkeyText;
        }
    }
}
