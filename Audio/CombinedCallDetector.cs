using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace AudioRecorderTrayApp
{
    public class CombinedCallDetector : IDisposable
    {
        public event Action? CallStarted;
        public event Action? CallEnded;
        private readonly System.Threading.Timer _monitorTimer;
        private readonly object _lock = new object ();
        private readonly string[] _targetAppProcessNames =
        {
            "discord",
            "zoom",
            "skype",
            "telegram",
            "teams"
        };
        private readonly string[] _browserProcessNames =
        {
            "chrome",
            "firefox",
            "msedge",
            "opera"
        };
        private readonly string[] _targetTitleKeywords =
        {
            "meet",
            "zoom",
            "teams"
        };
        private readonly MMDeviceEnumerator _enumerator = new MMDeviceEnumerator();
        private bool _isPaused = true;
        private string? _lastNotifiedCallKey;
        private string? _ignoredCallKey;
        public CombinedCallDetector()
        {
            _monitorTimer = new System.Threading.Timer(MonitorTick, null, Timeout.Infinite, Timeout.Infinite);
        }

        public void Start()
        {
            if (!_isPaused)
                return;
            _isPaused = false;
            _monitorTimer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
            Debug.WriteLine("[CombinedDetector] Детектор активирован.");
        }

        public void Pause()
        {
            _isPaused = true;
            _monitorTimer.Change(Timeout.Infinite, Timeout.Infinite);
            Debug.WriteLine("[CombinedDetector] Детектор поставлен на паузу.");
        }

        public void IgnoreCurrentCall()
        {
            _ignoredCallKey = _lastNotifiedCallKey;
            Debug.WriteLine($"[CombinedDetector] Пользователь проигнорировал звонок с ключом '{_ignoredCallKey}'.");
            Start();
        }

        private void MonitorTick(object? state)
        {
            if (_isPaused)
                return;
            lock (_lock)
            {
                string currentCallKey = NormalizeKey(FindCurrentCallIdentifier());
                if (!string.IsNullOrEmpty(currentCallKey) && currentCallKey.Equals(_ignoredCallKey))
                {
                    _lastNotifiedCallKey = currentCallKey;
                    return;
                }

                if (currentCallKey != _lastNotifiedCallKey)
                {
                    if (!string.IsNullOrEmpty(currentCallKey))
                    {
                        Debug.WriteLine($"[CombinedDetector] ОБНАРУЖЕН НОВЫЙ ЗВОНОК: {currentCallKey}");
                        _lastNotifiedCallKey = currentCallKey;
                        _ignoredCallKey = null;
                        Pause();
                        CallStarted?.Invoke();
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(_lastNotifiedCallKey))
                        {
                            Debug.WriteLine($"[CombinedDetector] Звонок '{_lastNotifiedCallKey}' завершился.");
                            CallEnded?.Invoke();
                        }

                        _lastNotifiedCallKey = null;
                        _ignoredCallKey = null;
                    }
                }
            }
        }

        private string? FindCurrentCallIdentifier()
        {
            var activeMicPids = GetActiveCaptureProcessIds();
            if (activeMicPids.Count == 0)
                return null;
            var allProcesses = Process.GetProcesses();
            foreach (var process in allProcesses)
            {
                if (activeMicPids.Contains((uint)process.Id) && _targetAppProcessNames.Any(p => process.ProcessName.Equals(p, StringComparison.OrdinalIgnoreCase)))
                {
                    return process.ProcessName;
                }
            }

            bool isAnyBrowserUsingMic = allProcesses.Any(p => _browserProcessNames.Any(name => p.ProcessName.Equals(name, StringComparison.OrdinalIgnoreCase)) && activeMicPids.Contains((uint)p.Id));
            if (!isAnyBrowserUsingMic)
                return null;
            foreach (var process in allProcesses.Where(p => _browserProcessNames.Contains(p.ProcessName, StringComparer.OrdinalIgnoreCase)))
            {
                try
                {
                    if (process.MainWindowHandle != IntPtr.Zero)
                    {
                        string windowTitle = process.MainWindowTitle;
                        if (!string.IsNullOrEmpty(windowTitle) && _targetTitleKeywords.Any(keyword => windowTitle.ToLowerInvariant().Contains(keyword)))
                        {
                            return windowTitle;
                        }
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        private HashSet<uint> GetActiveCaptureProcessIds()
        {
            var processIds = new HashSet<uint>();
            try
            {
                var devices = _enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
                foreach (var device in devices)
                {
                    using (device)
                    {
                        var sessionManager = device.AudioSessionManager;
                        for (int i = 0; i < sessionManager.Sessions.Count; i++)
                        {
                            var session = sessionManager.Sessions[i];
                            using (session)
                            {
                                try
                                {
                                    if (session.GetProcessID != 0 && session.State == AudioSessionState.AudioSessionStateActive && session.AudioMeterInformation.MasterPeakValue > 0.0001f)
                                    {
                                        processIds.Add(session.GetProcessID);
                                    }
                                }
                                catch
                                {
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            return processIds;
        }

        private string NormalizeKey(string? identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
                return string.Empty;
            string result = identifier.ToLowerInvariant();
            if (_targetAppProcessNames.Any(p => result.Contains(p)))
                return result;
            if (result.Contains("meet"))
                return "google meet";
            if (result.Contains("zoom"))
                return "zoom meeting";
            if (result.Contains("teams"))
                return "microsoft teams";
            return result.Trim();
        }

        public void Dispose()
        {
            _monitorTimer?.Dispose();
            _enumerator?.Dispose();
        }
    }
}
