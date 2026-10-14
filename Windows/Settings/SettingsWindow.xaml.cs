using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AudioRecorderTrayApp
{
    public partial class SettingsWindow : Window
    {
        private readonly Dictionary<string, System.Windows.Controls.UserControl> _pages = new Dictionary<string, System.Windows.Controls.UserControl>();
        public SettingsWindow()
        {
            InitializeComponent();
            InitializeNavigation();
        }

        private void InitializeNavigation()
        {
            _pages.Add("Общие", new GeneralSettingsView());
            _pages.Add("Горячие клавиши", new HotkeysSettingsView());
            NavigationListBox.ItemsSource = _pages.Keys;
            if (NavigationListBox.Items.Count > 0)
            {
                NavigationListBox.SelectedIndex = 0;
            }
        }

        private void NavigationListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NavigationListBox.SelectedItem is string selectedPageKey)
            {
                SettingsContentControl.Content = _pages[selectedPageKey];
            }
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
