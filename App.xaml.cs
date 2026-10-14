using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Supabase;
using Supabase.Gotrue;

namespace AudioRecorderTrayApp
{
    public partial class App : System.Windows.Application
    {
        [STAThread]
        public static void Main(string[] args)
        {
            Velopack.VelopackApp.Build().Run();
            var app = new App();
            app.InitializeComponent();
            app.Run();
        }

        public static Supabase.Client Supabase { get; private set; } = null !;

        private static readonly string _sessionFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioRecorderTrayApp", "session.json");
        private static bool _isShowingFatalError = false;
        protected override async void OnStartup(StartupEventArgs e)
        {
            RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.Default;
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            AppDomain.CurrentDomain.UnhandledException += (s, args) => ShowFatalError(args.ExceptionObject as Exception, "AppDomain");
            DispatcherUnhandledException += (s, args) =>
            {
                args.Handled = true;
                ShowFatalError(args.Exception, "Dispatcher");
            };
            Exit += OnAppExit;
            AppSettings.Load();
            var themeName = AppSettings.Current.Theme;
            var theme = themeName switch
            {
                "Темная" => Theme.Dark,
                "Системная" => Theme.System,
                _ => Theme.Light
            };
            ThemeManager.ApplyTheme(theme);
            await Services.DatabaseService.InitializeAsync();
            try
            {
                var url = Environment.GetEnvironmentVariable("SUPABASE_URL") ?? throw new InvalidOperationException("Set SUPABASE_URL before starting the application.");
                var key = Environment.GetEnvironmentVariable("SUPABASE_ANON_KEY") ?? throw new InvalidOperationException("Set SUPABASE_ANON_KEY before starting the application.");
                Supabase = new Supabase.Client(url, key, new Supabase.SupabaseOptions { AutoRefreshToken = true });
                SupabaseBaseUrl = url;
                await LoadSession();
                if (Supabase.Auth.CurrentUser == null)
                {
                    var loginWindow = new LoginWindow();
                    if (loginWindow.ShowDialog() != true)
                    {
                        this.Shutdown();
                        return;
                    }

                    await SaveSession(Supabase.Auth.CurrentSession);
                }

                var mainWindow = new MainWindow();
                MainWindow = mainWindow;
                var updateWindow = new UpdateWindow(mainWindow);
                updateWindow.Show();
            }
            catch (Exception ex)
            {
                ShowFatalError(ex, "OnStartup");
            }
        }

        public static string SupabaseBaseUrl { get; private set; } = string.Empty;

        private void OnAppExit(object sender, ExitEventArgs e)
        {
        }

        public static async Task SaveSession(Session? session)
        {
            if (session == null)
                return;
            try
            {
                var json = JsonSerializer.Serialize(session);
                Directory.CreateDirectory(Path.GetDirectoryName(_sessionFilePath)!);
                await File.WriteAllTextAsync(_sessionFilePath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка сохранения сессии: {ex.Message}");
            }
        }

        public static async Task LoadSession()
        {
            try
            {
                if (File.Exists(_sessionFilePath))
                {
                    var json = await File.ReadAllTextAsync(_sessionFilePath);
                    var session = JsonSerializer.Deserialize<Session>(json);
                    if (session != null && !string.IsNullOrEmpty(session.AccessToken) && !string.IsNullOrEmpty(session.RefreshToken))
                    {
                        await Supabase.Auth.SetSession(session.AccessToken, session.RefreshToken);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка загрузки сессии: {ex.Message}");
            }
        }

        public static void ClearSession()
        {
            try
            {
                if (File.Exists(_sessionFilePath))
                {
                    File.Delete(_sessionFilePath);
                }
            }
            catch
            {
            }
        }

        private void ShowFatalError(Exception? ex, string context)
        {
            if (_isShowingFatalError)
            {
                Debug.WriteLine("Предотвращена рекурсия в ShowFatalError");
                Environment.Exit(-1);
                return;
            }

            _isShowingFatalError = true;
            try
            {
                var errorMessage = $"Критическая ошибка ({context}):\n\n{ex?.ToString() ?? "Неизвестная ошибка."}";
                Debug.WriteLine($"ShowFatalError: {errorMessage}");
                try
                {
                    System.Windows.MessageBox.Show(errorMessage, "Критическая ошибка приложения", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch
                {
                    Console.WriteLine($"FATAL ERROR: {errorMessage}");
                }
            }
            finally
            {
                this.Shutdown();
            }
        }
    }
}
