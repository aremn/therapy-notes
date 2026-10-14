using System.Collections.Generic;
using System.Linq;
using System.Windows;
using AudioRecorderTrayApp.Models;
using AudioRecorderTrayApp.Services;

namespace AudioRecorderTrayApp
{
    public partial class ClientSelectionWindow : Window
    {
        public Client? SelectedClient { get; private set; }

        public ClientSelectionWindow()
        {
            InitializeComponent();
            this.Loaded += async (s, e) => await LoadClientsAsync();
        }

        private async Task LoadClientsAsync()
        {
            List<Client> clients = (await ClientStore.LoadAllAsync()).ToList();
            ClientsItemsControl.ItemsSource = clients;
        }

        private void ClientButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is Client client)
            {
                this.SelectedClient = client;
                CurrentClientService.CurrentClient = client;
                this.DialogResult = true;
                this.Close();
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}
