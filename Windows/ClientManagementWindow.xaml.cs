using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AudioRecorderTrayApp.Models;
using AudioRecorderTrayApp.Services;

namespace AudioRecorderTrayApp
{
    public partial class ClientManagementWindow : Window
    {
        private Client? _editingClient;
        public ClientManagementWindow()
        {
            InitializeComponent();
            ClientStore.ClientsChanged += LoadClients;
            this.Loaded += (s, e) => LoadClients();
        }

        protected override void OnClosed(EventArgs e)
        {
            ClientStore.ClientsChanged -= LoadClients;
            base.OnClosed(e);
        }

        private async void LoadClients()
        {
            try
            {
                var clients = await ClientStore.LoadAllAsync();
                ClientsList.ItemsSource = clients;
                StatusText.Text = $"Всего клиентов: {clients.Count}";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Ошибка загрузки: {ex.Message}";
            }
        }

        private void ClientsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ClientsList.SelectedItem is Client client)
            {
                StartEditClient(client);
            }
        }

        private void StartEditClient(Client client)
        {
            _editingClient = client;
            ClientNameTextBox.Text = client.Name;
            ClientDescriptionTextBox.Text = client.Description ?? string.Empty;
            NameCharacterCount.Text = $"{client.Name.Length}/50";
            DescriptionCharacterCount.Text = $"{(client.Description ?? string.Empty).Length}/300";
            EditPanelTitle.Text = "Редактировать клиента";
            SaveClientButton.Content = "Обновить";
            CancelEditButton.Visibility = Visibility.Visible;
        }

        private void CancelEdit_Click(object sender, RoutedEventArgs e)
        {
            ClearForm();
        }

        private void ClearForm()
        {
            _editingClient = null;
            ClientNameTextBox.Text = string.Empty;
            ClientDescriptionTextBox.Text = string.Empty;
            EditPanelTitle.Text = "Добавить клиента";
            SaveClientButton.Content = "Сохранить";
            CancelEditButton.Visibility = Visibility.Collapsed;
            ClientsList.SelectedItem = null;
        }

        private async void SaveClient_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var name = ClientNameTextBox.Text?.Trim();
                if (string.IsNullOrEmpty(name))
                {
                    System.Windows.MessageBox.Show("Введите имя клиента", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var existingClients = await ClientStore.LoadAllAsync();
                if (existingClients.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && c.Id != _editingClient?.Id))
                {
                    System.Windows.MessageBox.Show("Клиент с таким именем уже существует", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                Client client;
                if (_editingClient != null)
                {
                    client = _editingClient;
                    client.Name = name;
                    client.Description = string.IsNullOrWhiteSpace(ClientDescriptionTextBox.Text) ? null : ClientDescriptionTextBox.Text.Trim();
                }
                else
                {
                    client = new Client
                    {
                        Name = name,
                        Description = string.IsNullOrWhiteSpace(ClientDescriptionTextBox.Text) ? null : ClientDescriptionTextBox.Text.Trim(),
                        CreatedAtUtc = DateTime.UtcNow,
                        IsActive = true
                    };
                }

                await ClientStore.AddOrUpdateAsync(client);
                StatusText.Text = _editingClient != null ? $"Клиент '{name}' обновлен" : $"Клиент '{name}' добавлен";
                ClearForm();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Ошибка сохранения: {ex.Message}";
            }
        }

        private async void DeleteClient_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is System.Windows.Controls.Button button && button.Tag is string clientId)
                {
                    var client = await ClientStore.GetByIdAsync(clientId);
                    if (client == null)
                        return;
                    var result = System.Windows.MessageBox.Show($"Вы уверены, что хотите удалить клиента '{client.Name}'?\n\n" + "Это действие нельзя отменить. Сессии клиента останутся, но будут отвязаны от него.", "Подтверждение удаления", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (result == MessageBoxResult.Yes)
                    {
                        await ClientStore.RemoveAsync(clientId);
                        if (CurrentClientService.CurrentClient?.Id == clientId)
                        {
                            CurrentClientService.ClearCurrentClient();
                        }

                        StatusText.Text = $"Клиент '{client.Name}' удален";
                        ClearForm();
                    }
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Ошибка удаления: {ex.Message}";
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBox textBox)
            {
                var template = textBox.Template;
                var placeholderText = template.FindName("PlaceholderText", textBox) as TextBlock;
                if (placeholderText != null)
                {
                    placeholderText.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void TextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBox textBox)
            {
                var template = textBox.Template;
                var placeholderText = template.FindName("PlaceholderText", textBox) as TextBlock;
                if (placeholderText != null && string.IsNullOrEmpty(textBox.Text))
                {
                    placeholderText.Visibility = Visibility.Visible;
                }
            }
        }

        private void ClientNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBox textBox)
            {
                NameCharacterCount.Text = $"{textBox.Text.Length}/50";
            }
        }

        private void ClientDescriptionTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBox textBox)
            {
                DescriptionCharacterCount.Text = $"{textBox.Text.Length}/300";
                try
                {
                    if (textBox.CaretIndex == textBox.Text.Length)
                    {
                        textBox.ScrollToEnd();
                    }
                }
                catch
                {
                }
            }
        }
    }
}
