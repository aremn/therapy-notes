using System;
using System.Windows;
using System.Windows.Controls;

namespace AudioRecorderTrayApp.Windows.Dialogs
{
    public partial class AddClientWindow : Window
    {
        public string? ClientName { get; private set; }
        public string? ClientDescription { get; private set; }

        public AddClientWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            DescriptionTextBox.TextChanged += DescriptionTextBox_TextChanged;
            NameTextBox.TextChanged += NameTextBox_TextChanged;
            NameTextBox.Focus();
        }

        private void NameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var textBox = sender as System.Windows.Controls.TextBox;
            if (textBox != null && NameCharacterCount != null)
            {
                NameCharacterCount.Text = $"{textBox.Text.Length}/50";
            }
        }

        private void DescriptionTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                var tb = (System.Windows.Controls.TextBox)sender;
                if (DescriptionCharacterCount != null)
                {
                    DescriptionCharacterCount.Text = $"{tb.Text.Length}/300";
                }

                if (tb.CaretIndex == tb.Text.Length)
                {
                    tb.ScrollToEnd();
                }
            }
            catch
            {
            }
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            var name = NameTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                NameErrorText.Text = "Введите имя клиента";
                NameErrorText.Visibility = Visibility.Visible;
                NameTextBox.Focus();
                return;
            }

            NameErrorText.Visibility = Visibility.Collapsed;
            ClientName = name;
            ClientDescription = string.IsNullOrWhiteSpace(DescriptionTextBox.Text) ? null : DescriptionTextBox.Text.Trim();
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
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
    }
}
