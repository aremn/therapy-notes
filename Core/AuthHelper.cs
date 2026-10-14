using System;
using System.Linq;
using System.Threading.Tasks;

namespace AudioRecorderTrayApp.Core
{
    public static class AuthHelper
    {
        public static bool IsJwtExpiredError(Exception ex)
        {
            return ex is Postgrest.Exceptions.PostgrestException postgrestEx && (postgrestEx.Message.Contains("JWT expired") || postgrestEx.Message.Contains("PGRST303"));
        }

        public static async Task HandleJwtExpiredAsync(Exception ex)
        {
            try
            {
                Services.Logger.Error($"JWT token expired, redirecting to login: {ex.Message}");
                await App.Supabase.Auth.SignOut();
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    ShowLoginWindow();
                });
            }
            catch (Exception logoutEx)
            {
                Services.Logger.Error($"Error during JWT expired handling: {logoutEx.Message}");
            }
        }

        private static void ShowLoginWindow()
        {
            try
            {
                var windowsToClose = System.Windows.Application.Current.Windows.Cast<System.Windows.Window>().Where(w => w.GetType() != typeof(MainWindow)).ToList();
                foreach (var window in windowsToClose)
                {
                    window.Close();
                }

                var loginWindow = new AudioRecorderTrayApp.LoginWindow();
                loginWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                Services.Logger.Error($"Error showing login window: {ex.Message}");
            }
        }
    }
}
