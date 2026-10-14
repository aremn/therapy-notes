using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AudioRecorderTrayApp.Services
{
    public sealed class DualTrackRecorder : IDisposable
    {
        private WasapiCapture? _mic;
        private WasapiLoopbackCapture? _loopback;
        private WaveFileWriter? _micWriter;
        private WaveFileWriter? _loopWriter;
        private readonly Stopwatch _sw = new();
        public string? MicPath { get; private set; }
        public string? SystemPath { get; private set; }

        public async Task StartAsync()
        {
            Logger.Info("DualTrackRecorder.StartAsync");
            var appDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioRecorderTrayApp", "temp");
            Directory.CreateDirectory(appDir);
            MicPath = Path.Combine(appDir, $"mic_{Guid.NewGuid():N}.wav");
            SystemPath = Path.Combine(appDir, $"sys_{Guid.NewGuid():N}.wav");
            Logger.Info($"Paths: mic={MicPath}, sys={SystemPath}");
            MMDevice? micDevice = null;
            try
            {
                var enumerator = new MMDeviceEnumerator();
                if (!string.IsNullOrWhiteSpace(AppSettings.Current.SelectedMicrophoneId))
                {
                    micDevice = enumerator.GetDevice(AppSettings.Current.SelectedMicrophoneId);
                }
                else
                {
                    micDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DualTrackRecorder] Failed to get mic device: {ex}");
            }

            _mic = micDevice != null ? new WasapiCapture(micDevice) : new WasapiCapture();
            _loopback = new WasapiLoopbackCapture();
            _micWriter = new WaveFileWriter(MicPath!, _mic.WaveFormat);
            _loopWriter = new WaveFileWriter(SystemPath!, _loopback.WaveFormat);
            Logger.Info($"Formats: mic={_mic.WaveFormat}, loop={_loopback.WaveFormat}");
            _mic.DataAvailable += (s, a) =>
            {
                try
                {
                    _micWriter?.Write(a.Buffer, 0, a.BytesRecorded);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[DualTrackRecorder] mic write err: {ex.Message}");
                }
            };
            _loopback.DataAvailable += (s, a) =>
            {
                try
                {
                    _loopWriter?.Write(a.Buffer, 0, a.BytesRecorded);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[DualTrackRecorder] loop write err: {ex.Message}");
                }
            };
            _mic.RecordingStopped += (s, a) =>
            {
                try
                {
                    _micWriter?.Flush();
                }
                catch
                {
                }
            };
            _loopback.RecordingStopped += (s, a) =>
            {
                try
                {
                    _loopWriter?.Flush();
                }
                catch
                {
                }
            };
            _mic.StartRecording();
            _loopback.StartRecording();
            _sw.Restart();
            Logger.Info("DualTrackRecorder.Started");
            await Task.CompletedTask;
        }

        public async Task<(string? micPath, string? systemPath, int durationSeconds)> StopAsync()
        {
            Logger.Info("DualTrackRecorder.StopAsync");
            try
            {
                _mic?.StopRecording();
            }
            catch (Exception ex)
            {
                Logger.Warn($"mic stop: {ex.Message}");
            }

            try
            {
                _loopback?.StopRecording();
            }
            catch (Exception ex)
            {
                Logger.Warn($"loop stop: {ex.Message}");
            }

            _sw.Stop();
            await Task.Delay(200);
            DisposeWriters();
            DisposeCaptures();
            var duration = (int)Math.Round(_sw.Elapsed.TotalSeconds);
            Logger.Info($"DualTrackRecorder.Stopped duration={duration}s mic={MicPath} sys={SystemPath}");
            return (MicPath, SystemPath, duration);
        }

        private void DisposeWriters()
        {
            try
            {
                _micWriter?.Dispose();
            }
            catch
            {
            }

            try
            {
                _loopWriter?.Dispose();
            }
            catch
            {
            }

            _micWriter = null;
            _loopWriter = null;
        }

        private void DisposeCaptures()
        {
            try
            {
                _mic?.Dispose();
            }
            catch
            {
            }

            try
            {
                _loopback?.Dispose();
            }
            catch
            {
            }

            _mic = null;
            _loopback = null;
        }

        public void Dispose()
        {
            DisposeWriters();
            DisposeCaptures();
        }
    }
}
