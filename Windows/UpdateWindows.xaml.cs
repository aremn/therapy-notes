using System;
using System.Threading.Tasks;
using System.Windows;
using Velopack;
using Velopack.Sources;
using AudioRecorderTrayApp;

namespace AudioRecorderTrayApp
{
    public partial class UpdateWindow : Window
    {
        private readonly MainWindow _mainWindow;
        public UpdateWindow(MainWindow mainWindow)
        {
            InitializeComponent();
            _mainWindow = mainWindow;
            Loaded += async (s, e) => await CheckForUpdatesAsync();
        }

        private async Task CheckForUpdatesAsync()
        {
            FileLogger.Info("Запуск проверки обновлений Velopack...");
            try
            {
                var updateUrl = Environment.GetEnvironmentVariable("UPDATE_FEED_URL");
                if (string.IsNullOrWhiteSpace(updateUrl))
                {
                    LaunchMainWindow();
                    return;
                }

                FileLogger.Info($"URL для обновлений: {updateUrl}");
                var mgr = new UpdateManager(new SimpleWebSource(updateUrl));
                StatusText.Text = "Проверка наличия обновлений...";
                FileLogger.Info("Вызов mgr.CheckForUpdatesAsync()...");
                var updateInfo = await mgr.CheckForUpdatesAsync();
                FileLogger.Info("CheckForUpdatesAsync() завершен.");
                if (updateInfo != null)
                {
                    FileLogger.Info($"Найдено обновление: {updateInfo.TargetFullRelease.Version}");
                    StatusText.Text = $"Загрузка обновления v{updateInfo.TargetFullRelease.Version}...";
                    FileLogger.Info("Вызов mgr.DownloadUpdatesAsync()...");
                    await mgr.DownloadUpdatesAsync(updateInfo);
                    FileLogger.Info("DownloadUpdatesAsync() завершен.");
                    StatusText.Text = "Установка и перезапуск...";
                    await Task.Delay(1500);
                    FileLogger.Info("Вызов mgr.ApplyUpdatesAndRestart()...");
                    mgr.ApplyUpdatesAndRestart(updateInfo);
                }
                else
                {
                    FileLogger.Info("Обновления не найдены.");
                    StatusText.Text = "Обновления не найдены. Запуск...";
                    await Task.Delay(500);
                    LaunchMainWindow();
                }
            }
            catch (Exception ex)
            {
                FileLogger.Error("КРИТИЧЕСКАЯ ОШИБКА в процессе обновления!", ex);
                StatusText.Text = "Обновления не найдены. Запуск...";
                await Task.Delay(1500);
                LaunchMainWindow();
            }
        }

        private void LaunchMainWindow()
        {
            System.Windows.Application.Current.MainWindow = _mainWindow;
            _mainWindow.Show();
            this.Close();
        }
    }
}
