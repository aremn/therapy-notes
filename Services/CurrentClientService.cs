using System;
using System.Threading.Tasks;
using AudioRecorderTrayApp.Models;

namespace AudioRecorderTrayApp.Services
{
    public static class CurrentClientService
    {
        public static event Action<Client?>? CurrentClientChanged;
        private static Client? _currentClient;
        public static Client? CurrentClient
        {
            get => _currentClient;
            set
            {
                if (_currentClient?.Id != value?.Id)
                {
                    _currentClient = value;
                    CurrentClientChanged?.Invoke(_currentClient);
                    AppSettings.Current.SelectedClientId = _currentClient?.Id;
                    AppSettings.Save();
                }
            }
        }

        public static async Task LoadSelectedClientAsync()
        {
            try
            {
                var selectedClientId = AppSettings.Current.SelectedClientId;
                if (!string.IsNullOrEmpty(selectedClientId))
                {
                    var client = await ClientStore.GetByIdAsync(selectedClientId);
                    if (client != null)
                    {
                        CurrentClient = client;
                    }
                }
            }
            catch
            {
            }
        }

        public static void ClearCurrentClient()
        {
            CurrentClient = null;
        }
    }
}
