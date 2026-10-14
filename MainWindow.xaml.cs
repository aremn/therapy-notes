using NAudio.Wave;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using AudioRecorderTrayApp.Services;
using System.Reflection;

namespace AudioRecorderTrayApp
{
    public class SessionViewModel : INotifyPropertyChanged
    {
        private string _title = string.Empty;
        public string Id { get; set; } = string.Empty;

        public string Title
        {
            get => _title;
            set
            {
                if (_title != value)
                {
                    _title = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
                }
            }
        }

        public string CreatedAtLocal { get; set; } = string.Empty;
        public int DurationSeconds { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public partial class MainWindow : Window
    {
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("kernel32.dll")]
        private static extern uint GetLastError();
        private const int HOTKEY_ID = 9000;
        private HwndSource? _source;
        private IntPtr _windowHandle;
        private NotifyIcon? trayIcon;
        private bool isRecording = false;
        private readonly StringBuilder _fullTranscript = new StringBuilder();
        private DateTime _recordingStartUtc;
        private DualTrackRecorder? _dualRecorder;
        private RecordingIndicatorWindow? _indicatorWindow;
        private Icon? _defaultIcon;
        private Icon? _recordingIcon;
        private CombinedCallDetector? _callDetector;
        private string? _lastCompletedSessionId = null;
        public MainWindow()
        {
            InitializeComponent();
            DisplayAppVersion();
            CheckMicrophonePermission();
            AppSettings.Load();
            AppSettings.HotkeyChanged += OnHotkeyChanged;
            Loaded += MainWindow_Loaded;
            InitializeClientSystem();
            if (AppSettings.Current.AutoRecordOnCall)
            {
                InitializeCallDetector();
                _callDetector?.Start();
            }

            SetRecordingReady();
            try
            {
                _defaultIcon = (Icon)System.Drawing.SystemIcons.Application.Clone();
                _recordingIcon = (Icon)System.Drawing.SystemIcons.Information.Clone();
                trayIcon = new NotifyIcon
                {
                    Icon = _defaultIcon,
                    Visible = true,
                    Text = "Audio Recorder"
                };
                trayIcon.MouseClick += TrayIcon_MouseClick;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Критическая ошибка при создании иконки: {ex.Message}");
                System.Windows.Application.Current.Shutdown();
            }

            TextUploaderAnalysisEventsHook();
        }

        private void SetModelLoadingStatus()
        {
            try
            {
                if (FindName("RecordingStatusBorder")is Border border && FindName("RecordingStatusText")is TextBlock text && FindName("RecordingStatusDot")is Border dot)
                {
                    border.SetResourceReference(Border.BackgroundProperty, "StatusProcessingBackgroundBrush");
                    text.SetResourceReference(TextBlock.ForegroundProperty, "StatusProcessingForegroundBrush");
                    dot.SetResourceReference(Border.BackgroundProperty, "StatusProcessingDotBrush");
                    text.Text = "Загрузка модели…";
                }
            }
            catch
            {
            }
        }

        private void DisplayAppVersion()
        {
            try
            {
                var informationalVersion = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (!string.IsNullOrEmpty(informationalVersion))
                {
                    VersionText.Text = $"v{informationalVersion}";
                }
                else
                {
                    var version = Assembly.GetExecutingAssembly().GetName().Version;
                    if (version != null)
                    {
                        VersionText.Text = $"v{version.Major}.{version.Minor}.{version.Build}";
                    }
                    else
                    {
                        VersionText.Text = "";
                    }
                }
            }
            catch
            {
                VersionText.Text = "version error";
            }
        }

        private void TextUploaderAnalysisEventsHook()
        {
            try
            {
                TextUploader.AnalysisCompleted -= OnAnalysisCompleted;
                TextUploader.AnalysisCompleted += OnAnalysisCompleted;
                TextUploader.AnalysisFailed -= OnAnalysisFailed;
                TextUploader.AnalysisFailed += OnAnalysisFailed;
            }
            catch
            {
            }
        }

        private void OnAnalysisCompleted()
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    SetAnalysisReceivedStatus();
                    if (!string.IsNullOrEmpty(_lastCompletedSessionId))
                    {
                        var transcript = Services.DialogueStore.Load(_lastCompletedSessionId);
                        var cleanTranscript = Services.DialogueFormatter.RemoveLineNumbers(transcript ?? string.Empty);
                        OpenSessionReport(_lastCompletedSessionId, cleanTranscript);
                        _lastCompletedSessionId = null;
                    }

                    var timer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromSeconds(1)
                    };
                    timer.Tick += (s, e) =>
                    {
                        try
                        {
                            SetRecordingReady();
                        }
                        catch
                        {
                        }

                        try
                        {
                            (s as System.Windows.Threading.DispatcherTimer)?.Stop();
                        }
                        catch
                        {
                        }
                    };
                    timer.Start();
                }
                catch
                {
                }
            });
        }

        private void OnAnalysisFailed()
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    SetRecordingReady();
                }
                catch
                {
                }
            });
        }

        public async Task ToggleRecordingAsync()
        {
            if (isRecording)
            {
                isRecording = false;
                SetRecordingStatus(false);
                _indicatorWindow?.StopTimerAndClose();
                _indicatorWindow = null;
                UpdateMenuText();
                if (trayIcon != null)
                    trayIcon.Icon = _defaultIcon;
                new NotificationWindow("Транскрибация остановлена.").Show();
                if (AppSettings.Current.AutoRecordOnCall)
                    _callDetector?.Start();
                Logger.Info("Recording stop requested");
                int durationSeconds = Math.Max(0, (int)Math.Round((DateTime.UtcNow - _recordingStartUtc).TotalSeconds));
                try
                {
                    if (_dualRecorder != null)
                    {
                        var(micPath, sysPath, measuredDur) = await _dualRecorder.StopAsync();
                        Logger.Info($"Stopped: mic={micPath} sys={sysPath} measuredDur={measuredDur}");
                        _dualRecorder = null;
                        if (micPath == null || sysPath == null)
                            throw new Exception("Не удалось получить пути записанных файлов.");
                        if (measuredDur > 0)
                            durationSeconds = measuredDur;
                        Logger.Info("Running offline pipeline...");
                        try
                        {
                            SetPreparingStatus();
                        }
                        catch
                        {
                        }

                        var result = await UploadOrchestrator.ProcessAndUploadAsync(micPath, sysPath, durationSeconds);
                        Logger.Info($"Offline pipeline finished id={result.Id} dur={result.DurationSeconds}");
                        _lastCompletedSessionId = result.Id;
                        try
                        {
                            System.Windows.Clipboard.SetText(result.Id ?? string.Empty);
                        }
                        catch
                        {
                        }

                        new NotificationWindow($"Сессия сохранена локально. ID скопирован в буфер: {result.Id}").Show();
                        try
                        {
                            SetSentWaitingStatus();
                        }
                        catch
                        {
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Offline pipeline error: {ex}");
                    Debug.WriteLine($"[MainWindow] Ошибка оффлайн пайплайна: {ex}");
                    new NotificationWindow($"Ошибка обработки: {ex.Message}").Show();
                }

                return;
            }

            var currentClient = Services.CurrentClientService.CurrentClient;
            if (currentClient == null || currentClient.Id == "ALL_CLIENTS")
            {
                var clientSelectionWindow = new ClientSelectionWindow();
                if (clientSelectionWindow.ShowDialog() != true)
                {
                    return;
                }
            }

            if (isRecording)
                return;
            Logger.Info("Recording start requested");
            _callDetector?.Pause();
            isRecording = true;
            SetRecordingStatus(true);
            _recordingStartUtc = DateTime.UtcNow;
            _indicatorWindow = new RecordingIndicatorWindow(this);
            _indicatorWindow.Show();
            UpdateMenuText();
            if (trayIcon != null)
                trayIcon.Icon = _recordingIcon;
            new NotificationWindow("Транскрибация началась...").Show();
            _dualRecorder = new DualTrackRecorder();
            try
            {
                await _dualRecorder.StartAsync();
                Logger.Info("DualTrackRecorder started");
            }
            catch (Exception ex)
            {
                Logger.Error($"Start dual recorder failed: {ex}");
                Debug.WriteLine($"[MainWindow] Ошибка запуска двойной записи: {ex}");
                new NotificationWindow($"Ошибка записи: {ex.Message}").Show();
                isRecording = false;
                SetRecordingStatus(false);
                UpdateMenuText();
                if (trayIcon != null)
                    trayIcon.Icon = _defaultIcon;
            }
        }

        private void SetRecordingStatus(bool recording)
        {
            try
            {
                if (FindName("RecordingStatusBorder")is Border border && FindName("RecordingStatusText")is TextBlock text && FindName("RecordingStatusDot")is Border dot)
                {
                    if (recording)
                    {
                        border.SetResourceReference(Border.BackgroundProperty, "StatusRecordingBackgroundBrush");
                        text.SetResourceReference(TextBlock.ForegroundProperty, "StatusRecordingForegroundBrush");
                        dot.SetResourceReference(Border.BackgroundProperty, "StatusRecordingDotBrush");
                        text.Text = "Идёт запись";
                    }
                    else
                    {
                        border.SetResourceReference(Border.BackgroundProperty, "StatusReadyBackgroundBrush");
                        text.SetResourceReference(TextBlock.ForegroundProperty, "StatusReadyForegroundBrush");
                        dot.SetResourceReference(Border.BackgroundProperty, "StatusReadyDotBrush");
                        text.Text = "Готов к записи";
                    }
                }
            }
            catch
            {
            }
        }

        private void SetProcessingStatus()
        {
            try
            {
                if (FindName("RecordingStatusBorder")is Border border && FindName("RecordingStatusText")is TextBlock text && FindName("RecordingStatusDot")is Border dot)
                {
                    border.SetResourceReference(Border.BackgroundProperty, "StatusProcessingBackgroundBrush");
                    text.SetResourceReference(TextBlock.ForegroundProperty, "StatusProcessingForegroundBrush");
                    dot.SetResourceReference(Border.BackgroundProperty, "StatusProcessingDotBrush");
                    text.Text = "Обработка";
                }
            }
            catch
            {
            }
        }

        private void SetPreparingStatus()
        {
            try
            {
                if (FindName("RecordingStatusBorder")is Border border && FindName("RecordingStatusText")is TextBlock text && FindName("RecordingStatusDot")is Border dot)
                {
                    border.SetResourceReference(Border.BackgroundProperty, "StatusProcessingBackgroundBrush");
                    text.SetResourceReference(TextBlock.ForegroundProperty, "StatusProcessingForegroundBrush");
                    dot.SetResourceReference(Border.BackgroundProperty, "StatusProcessingDotBrush");
                    text.Text = "Готовится к отправке";
                }
            }
            catch
            {
            }
        }

        private void SetSentWaitingStatus()
        {
            try
            {
                if (FindName("RecordingStatusBorder")is Border border && FindName("RecordingStatusText")is TextBlock text && FindName("RecordingStatusDot")is Border dot)
                {
                    border.SetResourceReference(Border.BackgroundProperty, "StatusProcessingBackgroundBrush");
                    text.SetResourceReference(TextBlock.ForegroundProperty, "StatusProcessingForegroundBrush");
                    dot.SetResourceReference(Border.BackgroundProperty, "StatusProcessingDotBrush");
                    text.Text = "Отправлено, ждём результат";
                }
            }
            catch
            {
            }
        }

        private void SetAnalysisReceivedStatus()
        {
            try
            {
                if (FindName("RecordingStatusBorder")is Border border && FindName("RecordingStatusText")is TextBlock text && FindName("RecordingStatusDot")is Border dot)
                {
                    border.SetResourceReference(Border.BackgroundProperty, "StatusReadyBackgroundBrush");
                    text.SetResourceReference(TextBlock.ForegroundProperty, "StatusReadyForegroundBrush");
                    dot.SetResourceReference(Border.BackgroundProperty, "StatusReadyDotBrush");
                    text.Text = "Анализ получен!";
                }
            }
            catch
            {
            }
        }

        private void SetRecordingReady()
        {
            try
            {
                if (FindName("RecordingStatusBorder")is Border border && FindName("RecordingStatusText")is TextBlock text && FindName("RecordingStatusDot")is Border dot)
                {
                    border.SetResourceReference(Border.BackgroundProperty, "StatusReadyBackgroundBrush");
                    text.SetResourceReference(TextBlock.ForegroundProperty, "StatusReadyForegroundBrush");
                    dot.SetResourceReference(Border.BackgroundProperty, "StatusReadyDotBrush");
                    text.Text = "Готов к записи";
                }
            }
            catch
            {
            }
        }

        private void RecordMenuItem_Click(object sender, RoutedEventArgs e)
        {
            _ = ToggleRecordingAsync();
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                _ = ToggleRecordingAsync();
                handled = true;
            }

            return IntPtr.Zero;
        }

        private void OnCallDetected()
        {
            Debug.WriteLine("[MainWindow] OnCallDetected вызван");
            try
            {
                Debug.WriteLine("[MainWindow] OnCallDetected - вызываем Dispatcher.Invoke");
                Dispatcher.Invoke(() =>
                {
                    Debug.WriteLine("[MainWindow] OnCallDetected - внутри Dispatcher.Invoke");
                    if (isRecording || System.Windows.Application.Current.Windows.OfType<ConfirmationWindow>().Any())
                    {
                        Debug.WriteLine("[MainWindow] OnCallDetected - уже записываем или окно открыто, выходим");
                        return;
                    }

                    Debug.WriteLine("[MainWindow] OnCallDetected - создаем ConfirmationWindow");
                    var confirmationWindow = new ConfirmationWindow();
                    Debug.WriteLine("[MainWindow] OnCallDetected - показываем диалог");
                    if (confirmationWindow.ShowDialog() == true)
                    {
                        Debug.WriteLine("[MainWindow] OnCallDetected - пользователь согласился, начинаем запись");
                        var windowLeft = confirmationWindow.WindowLeft;
                        var windowTop = confirmationWindow.WindowTop;
                        var currentClient = Services.CurrentClientService.CurrentClient;
                        if (currentClient == null || currentClient.Id == "ALL_CLIENTS")
                        {
                            var clientSelectionWindow = new ClientSelectionWindow();
                            clientSelectionWindow.Owner = this;
                            clientSelectionWindow.WindowStartupLocation = WindowStartupLocation.Manual;
                            clientSelectionWindow.Left = windowLeft;
                            clientSelectionWindow.Top = windowTop;
                            if (clientSelectionWindow.ShowDialog() != true)
                            {
                                Debug.WriteLine("[MainWindow] OnCallDetected - пользователь отменил выбор клиента");
                                _callDetector?.IgnoreCurrentCall();
                                return;
                            }
                        }

                        _ = ToggleRecordingAsync();
                    }
                    else
                    {
                        Debug.WriteLine("[MainWindow] OnCallDetected - пользователь отклонил, игнорируем звонок");
                        _callDetector?.IgnoreCurrentCall();
                    }

                    Debug.WriteLine("[MainWindow] OnCallDetected - Dispatcher.Invoke завершен");
                });
                Debug.WriteLine("[MainWindow] OnCallDetected завершен");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] ОШИБКА в OnCallDetected: {ex.Message}");
                Debug.WriteLine($"[MainWindow] StackTrace: {ex.StackTrace}");
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ShowInTaskbar = true;
            WindowState = WindowState.Normal;
            Visibility = Visibility.Visible;
            TryLoadSessions();
            SessionStore.SessionsChanged += OnSessionsChanged;
            if (!isRecording)
                SetRecordingReady();
        }

        private void OnSessionsChanged()
        {
            try
            {
                Dispatcher.Invoke(() => TryLoadSessions());
            }
            catch
            {
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var helper = new WindowInteropHelper(this);
            _windowHandle = helper.Handle;
            _source = HwndSource.FromHwnd(_windowHandle);
            _source?.AddHook(HwndHook);
            RegisterHotKey();
        }

        private void OnHotkeyChanged(HotkeySettings newHotkey)
        {
            UnregisterHotKey();
            RegisterHotKey(newHotkey);
        }

        private void RegisterHotKey(HotkeySettings hotkey)
        {
            uint modifiers = (uint)hotkey.Modifiers;
            uint vk = (uint)KeyInterop.VirtualKeyFromKey(hotkey.Key);
            RegisterHotKey(_windowHandle, HOTKEY_ID, modifiers, vk);
        }

        private void RegisterHotKey()
        {
            RegisterHotKey(AppSettings.Current.Hotkey);
        }

        private void UnregisterHotKey()
        {
            UnregisterHotKey(_windowHandle, HOTKEY_ID);
        }

        private void TrayIcon_MouseClick(object? sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                if (ContextMenu != null)
                {
                    ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                    ContextMenu.IsOpen = true;
                }
            }
            else if (e.Button == MouseButtons.Left)
            {
                RestoreWindow();
            }
        }

        private void UpdateMenuText()
        {
            if (ContextMenu != null)
            {
                var mi = ContextMenu.Items.OfType<MenuItem>().FirstOrDefault(m => string.Equals(m.Name, "recordMenuItem", StringComparison.Ordinal));
                if (mi != null)
                    mi.Header = isRecording ? "Остановить транскрибацию" : "Начать транскрибацию";
            }

            if (FindName("RecordButton")is System.Windows.Controls.Button btn)
            {
                btn.Content = isRecording ? "Остановить транскрибацию" : "Начать транскрибацию";
            }
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
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

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_shutdown)
            {
                e.Cancel = true;
                Hide();
            }

            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            _callDetector?.Dispose();
            AppSettings.HotkeyChanged -= OnHotkeyChanged;
            SessionStore.SessionsChanged -= OnSessionsChanged;
            _source?.RemoveHook(HwndHook);
            UnregisterHotKey();
            trayIcon?.Dispose();
            try
            {
                TextUploader.AnalysisCompleted -= OnAnalysisCompleted;
                TextUploader.AnalysisFailed -= OnAnalysisFailed;
            }
            catch
            {
            }

            base.OnClosed(e);
        }

        private void SettingsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            new SettingsWindow().ShowDialog();
        }

        private void OpenAdmin_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                new AdminWindow().Show();
            }
            catch
            {
            }
        }

        private bool _shutdown = false;
        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            _shutdown = true;
            System.Windows.Application.Current.Shutdown();
        }

        private void RestoreWindowMenuItem_Click(object sender, RoutedEventArgs e)
        {
            RestoreWindow();
        }

        private void RestoreWindow()
        {
            try
            {
                Show();
                ShowInTaskbar = true;
                WindowState = WindowState.Normal;
                Activate();
                Topmost = true;
                Topmost = false;
            }
            catch
            {
            }
        }

        private void TryLoadSessions()
        {
            try
            {
                var allSessions = SessionStore.LoadAll();
                var currentClient = CurrentClientService.CurrentClient;
                List<SessionStore.SessionMeta> filteredSessions;
                if (currentClient != null && currentClient.Id != "ALL_CLIENTS")
                {
                    filteredSessions = allSessions.Where(s => s.ClientId == currentClient.Id || s.ClientId == currentClient.Name || string.Equals(s.ClientId, currentClient.Id, StringComparison.OrdinalIgnoreCase) || string.Equals(s.ClientId, currentClient.Name, StringComparison.OrdinalIgnoreCase)).ToList();
                }
                else
                {
                    filteredSessions = allSessions.ToList();
                }

                var sessions = filteredSessions.Select(s => new SessionViewModel { Id = s.Id, Title = s.Title ?? $"Сессия {s.CreatedAtUtc.ToLocalTime():dd.MM.yyyy HH:mm}", CreatedAtLocal = s.CreatedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm"), DurationSeconds = s.DurationSeconds }).ToList();
                if (this.FindName("SessionsList")is System.Windows.Controls.ListView lv)
                {
                    lv.ItemsSource = sessions;
                    lv.MouseDoubleClick -= SessionsList_DoubleClick;
                    lv.MouseDoubleClick += SessionsList_DoubleClick;
                }

                UpdateStatistics(filteredSessions);
            }
            catch
            {
            }
        }

        private void UpdateStatistics(IReadOnlyList<SessionStore.SessionMeta> sessions)
        {
            try
            {
                var totalSessionsText = this.FindName("TotalSessionsText") as TextBlock;
                var totalTimeText = this.FindName("TotalTimeText") as TextBlock;
                var lastSessionText = this.FindName("LastSessionText") as TextBlock;
                if (totalSessionsText != null)
                    totalSessionsText.Text = sessions.Count.ToString();
                if (totalTimeText != null)
                {
                    var totalSeconds = sessions.Sum(s => s.DurationSeconds);
                    var converter = new Converters.HumanDurationConverter();
                    var formattedTime = converter.Convert(totalSeconds, typeof(string), new object (), System.Globalization.CultureInfo.CurrentCulture) as string;
                    totalTimeText.Text = formattedTime ?? "0с";
                }

                if (lastSessionText != null)
                {
                    if (sessions.Any())
                    {
                        var lastSession = sessions.OrderByDescending(s => s.CreatedAtUtc).First();
                        var timeAgo = DateTime.UtcNow - lastSession.CreatedAtUtc;
                        if (timeAgo.TotalDays < 1)
                            lastSessionText.Text = "Сегодня";
                        else if (timeAgo.TotalDays < 7)
                            lastSessionText.Text = $"{(int)timeAgo.TotalDays} дн. назад";
                        else
                            lastSessionText.Text = lastSession.CreatedAtUtc.ToLocalTime().ToString("dd.MM.yyyy");
                    }
                    else
                    {
                        lastSessionText.Text = "—";
                    }
                }
            }
            catch
            {
                if (FindName("TotalSessionsText")is TextBlock t1)
                    t1.Text = "0";
                if (FindName("TotalTimeText")is TextBlock t2)
                    t2.Text = "0с";
                if (FindName("LastSessionText")is TextBlock t3)
                    t3.Text = "—";
            }
        }

        private void SessionsList_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Controls.ListView lv && lv.SelectedItem != null)
            {
                try
                {
                    var idProp = lv.SelectedItem.GetType().GetProperty("Id");
                    var id = idProp?.GetValue(lv.SelectedItem) as string;
                    if (string.IsNullOrWhiteSpace(id))
                        return;
                    var text = DialogueStore.Load(id) ?? string.Empty;
                    var cleanText = Services.DialogueFormatter.RemoveLineNumbers(text);
                    OpenSessionReport(id, cleanText);
                }
                catch
                {
                }
            }
        }

        private void SessionsList_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Right)
            {
                try
                {
                    if (sender is System.Windows.Controls.ListView lv && lv.SelectedItem != null)
                    {
                        var idProp = lv.SelectedItem.GetType().GetProperty("Id");
                        var id = idProp?.GetValue(lv.SelectedItem) as string;
                        if (string.IsNullOrWhiteSpace(id))
                            return;
                        var text = DialogueStore.Load(id) ?? string.Empty;
                        var cleanText = Services.DialogueFormatter.RemoveLineNumbers(text);
                        OpenSessionReport(id, cleanText);
                        e.Handled = true;
                    }
                }
                catch
                {
                }
            }
        }

        private void SessionItemButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is System.Windows.Controls.Button btn && btn.Tag is string id)
                {
                    var text = DialogueStore.Load(id) ?? string.Empty;
                    var cleanText = Services.DialogueFormatter.RemoveLineNumbers(text);
                    OpenSessionReport(id, cleanText);
                }
            }
            catch
            {
            }
        }

        private void SessionItem_Click(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (sender is Border border && border.Tag is string id)
                {
                    var text = DialogueStore.Load(id) ?? string.Empty;
                    var cleanText = Services.DialogueFormatter.RemoveLineNumbers(text);
                    OpenSessionReport(id, cleanText);
                }
            }
            catch
            {
            }
        }

        private void AllSessionsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var calendarView = new SessionsCalendarView();
                try
                {
                    ReportContentControl.Content = calendarView;
                    PlaceholderView.Visibility = Visibility.Collapsed;
                    ReportView.Visibility = Visibility.Visible;
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Не удалось открыть календарь: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    Debug.WriteLine($"Error opening calendar view: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error opening calendar: {ex.Message}");
            }
        }

        private void TitleTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is System.Windows.Controls.TextBox textBox && textBox.Tag is string id)
                {
                    SaveSessionTitle(id, textBox.Text);
                }
            }
            catch
            {
            }
        }

        private void TitleTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            try
            {
                if (e.Key == Key.Enter && sender is System.Windows.Controls.TextBox textBox && textBox.Tag is string id)
                {
                    SaveSessionTitle(id, textBox.Text);
                    textBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                }
            }
            catch
            {
            }
        }

        private void SaveSessionTitle(string sessionId, string newTitle)
        {
            try
            {
                var sessions = SessionStore.LoadAll().ToList();
                var session = sessions.FirstOrDefault(s => s.Id == sessionId);
                if (session != null)
                {
                    session.Title = string.IsNullOrWhiteSpace(newTitle) ? $"Сессия {session.CreatedAtUtc.ToLocalTime():dd.MM.yyyy HH:mm}" : newTitle.Trim();
                    Services.SessionStore.AddOrUpdate(session);
                }
            }
            catch
            {
            }
        }

        private void InitializeCallDetector()
        {
            Debug.WriteLine("[MainWindow] Начинаем инициализацию CallDetector");
            try
            {
                Debug.WriteLine("[MainWindow] Создаем новый CombinedCallDetector");
                _callDetector = new CombinedCallDetector();
                Debug.WriteLine("[MainWindow] CombinedCallDetector создан, добавляем обработчик события");
                _callDetector.CallStarted += OnCallDetected;
                Debug.WriteLine("[MainWindow] CallDetector инициализирован успешно");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] ОШИБКА при инициализации CallDetector: {ex.Message}");
                Debug.WriteLine($"[MainWindow] StackTrace: {ex.StackTrace}");
                throw;
            }
        }

        public void ApplyAutoRecordSetting()
        {
            try
            {
                if (AppSettings.Current.AutoRecordOnCall)
                {
                    if (_callDetector == null)
                    {
                        InitializeCallDetector();
                    }

                    _callDetector?.Start();
                }
                else
                {
                    _callDetector?.Dispose();
                    _callDetector = null;
                }
            }
            catch
            {
            }
        }

        private void CheckMicrophonePermission()
        {
            if (WaveInEvent.DeviceCount < 1)
            {
                ShowPermissionDialogAndShutdown("Для работы приложению требуется доступ к микрофону, но он отключен в системных настройках Windows.", null);
                return;
            }

            try
            {
                using var waveIn = new WaveInEvent
                {
                    DeviceNumber = 0
                };
                waveIn.StartRecording();
                waveIn.StopRecording();
            }
            catch (Exception ex)
            {
                ShowPermissionDialogAndShutdown("При попытке инициализировать микрофон произошла ошибка. Это может быть связано с правами доступа или проблемой с драйвером.", ex.Message);
            }
        }

        private void ShowPermissionDialogAndShutdown(string message, string? systemError)
        {
            var dialog = new PermissionDialog(message, systemError ?? "");
            dialog.ShowDialog();
            System.Windows.Application.Current.Shutdown();
        }

        private void ThemeToggle_Click(object sender, RoutedEventArgs e)
        {
            var currentTheme = AppSettings.Current.Theme;
            var newTheme = currentTheme == "Темная" ? "Светлая" : "Темная";
            AppSettings.Current.Theme = newTheme;
            AppSettings.Save();
            var theme = newTheme switch
            {
                "Темная" => Theme.Dark,
                "Системная" => Theme.System,
                _ => Theme.Light
            };
            ThemeManager.ApplyTheme(theme);
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            new SettingsWindow().ShowDialog();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                WindowState = WindowState.Minimized;
            }
            catch
            {
            }
        }

        private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }
            catch
            {
            }
        }

        public void OpenSessionReport(string sessionId, string transcriptText)
        {
            Debug.WriteLine($"OpenSessionReport called with sessionId: {sessionId}");
            try
            {
                var reportView = new SessionReportView(sessionId, transcriptText);
                if (FindName("ReportContentControl")is ContentControl cc)
                    cc.Content = reportView;
                if (FindName("PlaceholderView")is Grid pv)
                    pv.Visibility = Visibility.Collapsed;
                if (FindName("ReportView")is Grid rv2)
                    rv2.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error showing session report view: {ex.Message}");
                CloseReportView();
            }
        }

        private void CloseReportView()
        {
            try
            {
                if (this.FindName("ReportContentControl")is ContentControl cc)
                    cc.Content = null;
                if (this.FindName("ReportView")is Grid rv)
                    rv.Visibility = Visibility.Collapsed;
                if (this.FindName("PlaceholderView")is Grid pv)
                    pv.Visibility = Visibility.Visible;
            }
            catch
            {
            }
        }

        private void OnCloseReportViewButton_Click(object sender, RoutedEventArgs e)
        {
            CloseReportView();
        }

        private void InitializeClientSystem()
        {
            try
            {
                CurrentClientService.CurrentClientChanged += OnCurrentClientChanged;
                ClientStore.ClientsChanged += async () => await LoadClientsAsync();
                _ = LoadClientsAsync();
                _ = CurrentClientService.LoadSelectedClientAsync();
                if (CurrentClientService.CurrentClient == null)
                {
                    Dispatcher.InvokeAsync(async () =>
                    {
                        var clients = await ClientStore.LoadAllAsync();
                        var firstClient = clients.FirstOrDefault();
                        if (firstClient != null)
                        {
                            CurrentClientService.CurrentClient = firstClient;
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error initializing client system: {ex.Message}");
            }
        }

        private async void AddClient_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new Windows.Dialogs.AddClientWindow
                {
                    Owner = this
                };
                var result = dialog.ShowDialog();
                if (result == true && !string.IsNullOrWhiteSpace(dialog.ClientName))
                {
                    var clientName = dialog.ClientName!.Trim();
                    var clientDescription = string.IsNullOrWhiteSpace(dialog.ClientDescription) ? null : dialog.ClientDescription!.Trim();
                    var existingClients = await ClientStore.LoadAllAsync();
                    if (existingClients.Any(c => c.Name.Equals(clientName, StringComparison.OrdinalIgnoreCase)))
                    {
                        System.Windows.MessageBox.Show("Клиент с таким именем уже существует", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    var newClient = new Models.Client
                    {
                        Name = clientName,
                        Description = clientDescription,
                        CreatedAtUtc = DateTime.UtcNow,
                        IsActive = true
                    };
                    await ClientStore.AddOrUpdateAsync(newClient);
                    CurrentClientService.CurrentClient = newClient;
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Ошибка добавления клиента: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task LoadClientsAsync()
        {
            try
            {
                var clients = (await ClientStore.LoadAllAsync()).ToList();
                var allClientsOption = new Models.Client
                {
                    Id = "ALL_CLIENTS",
                    Name = "Все клиенты",
                    Description = "Просмотр всех сессий (запись недоступна)",
                    IsActive = true
                };
                clients.Insert(0, allClientsOption);
                Dispatcher.Invoke(() =>
                {
                    ClientsComboBox.ItemsSource = clients;
                    var currentClient = CurrentClientService.CurrentClient;
                    if (currentClient != null)
                    {
                        ClientsComboBox.SelectedItem = clients.FirstOrDefault(c => c.Id == currentClient.Id);
                        OnCurrentClientChanged(currentClient);
                    }
                    else
                    {
                        ClientsComboBox.SelectedItem = allClientsOption;
                        OnCurrentClientChanged(allClientsOption);
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading clients: {ex.Message}");
            }
        }

        private void OnCurrentClientChanged(Models.Client? client)
        {
            Dispatcher.Invoke(() =>
            {
                if (client != null)
                {
                    NoClientText.Visibility = Visibility.Collapsed;
                    ClientInfoHeader.Visibility = Visibility.Visible;
                    if (!string.IsNullOrWhiteSpace(client.Description))
                    {
                        CurrentClientDescription.Text = client.Description;
                        CurrentClientDescription.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        CurrentClientDescription.Text = "Описание отсутствует";
                        CurrentClientDescription.Visibility = Visibility.Visible;
                    }

                    ClientsComboBox.SelectedItem = ClientsComboBox.Items.Cast<Models.Client>().FirstOrDefault(c => c.Id == client.Id);
                }
                else
                {
                    ClientInfoHeader.Visibility = Visibility.Collapsed;
                    CurrentClientDescription.Visibility = Visibility.Collapsed;
                    NoClientText.Visibility = Visibility.Visible;
                    ClientsComboBox.SelectedItem = null;
                }

                TryLoadSessions();
            });
        }

        private void ClientsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (ClientsComboBox.SelectedItem is Models.Client selectedClient)
                {
                    CurrentClientService.CurrentClient = selectedClient;
                }
                else if (ClientsComboBox.SelectedItem == null)
                {
                    CurrentClientService.ClearCurrentClient();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error changing current client: {ex.Message}");
            }
        }

        private void ManageClients_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var clientManagementWindow = new ClientManagementWindow
                {
                    Owner = this
                };
                clientManagementWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Ошибка открытия управления клиентами: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
