using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NAudio.Lame;
using System.Diagnostics;
using System.IO;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using NAudio.CoreAudioApi;

namespace AudioRecorderTrayApp
{
    public class AudioMixer
    {
        private IWaveIn? _microphoneSource;
        private IWaveIn? _systemAudioSource;
        private WaveFileWriter? _micWriter;
        private WaveFileWriter? _systemWriter;
        private string? _tempMicPath;
        private string? _tempSystemPath;
        public string? OutputFilePath { get; private set; }

        public void StartRecording()
        {
            try
            {
                var enumerator = new MMDeviceEnumerator();
                MMDevice microphoneDevice;
                if (!string.IsNullOrEmpty(AppSettings.Current.SelectedMicrophoneId))
                {
                    microphoneDevice = enumerator.GetDevice(AppSettings.Current.SelectedMicrophoneId);
                }
                else
                {
                    microphoneDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                }

                _microphoneSource = new WasapiCapture(microphoneDevice);
                _tempMicPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_mic.wav");
                _micWriter = new WaveFileWriter(_tempMicPath, _microphoneSource.WaveFormat);
                _microphoneSource.DataAvailable += (s, e) => _micWriter?.Write(e.Buffer, 0, e.BytesRecorded);
                _systemAudioSource = new WasapiLoopbackCapture();
                _tempSystemPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_system.wav");
                _systemWriter = new WaveFileWriter(_tempSystemPath, _systemAudioSource.WaveFormat);
                _systemAudioSource.DataAvailable += (s, e) => _systemWriter?.Write(e.Buffer, 0, e.BytesRecorded);
                _microphoneSource.StartRecording();
                _systemAudioSource.StartRecording();
                Debug.WriteLine("[AudioMixer] ЗАПИСЬ НАЧАЛАСЬ (в два временных файла WAV)!");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"!!!!!! ОШИБКА В StartRecording: {ex.ToString()}");
                CleanupRecording();
                System.Windows.Application.Current.Dispatcher.Invoke(() => new NotificationWindow($"Ошибка запуска записи: {ex.Message}").Show());
            }
        }

        public async Task<string?> StopRecordingAndGetPath()
        {
            Debug.WriteLine("[AudioMixer] Начинаю StopRecording()...");
            _microphoneSource?.StopRecording();
            _systemAudioSource?.StopRecording();
            await Task.Delay(500);
            CleanupRecording();
            Debug.WriteLine("[AudioMixer] Запись в файлы остановлена, источники очищены.");
            await Task.Run(() => MixAndEncode());
            return OutputFilePath;
        }

        private void MixAndEncode()
        {
            try
            {
                if (!File.Exists(_tempMicPath) || new FileInfo(_tempMicPath!).Length < 1024)
                {
                    Debug.WriteLine("[AudioMixer] Файл микрофона отсутствует или пуст. Сведение отменено.");
                    return;
                }

                if (!File.Exists(_tempSystemPath) || new FileInfo(_tempSystemPath!).Length < 1024)
                {
                    Debug.WriteLine("[AudioMixer] Файл системных звуков отсутствует или пуст. Сведение отменено.");
                    return;
                }

                Debug.WriteLine("[AudioMixer] Начинаю сведение и кодирование...");
                using (var micReader = new AudioFileReader(_tempMicPath!))
                using (var systemReader = new AudioFileReader(_tempSystemPath!))
                {
                    ISampleProvider ConvertToStandardFormat(ISampleProvider source, float volume)
                    {
                        const int targetSampleRate = 44100;
                        const int targetChannels = 2;
                        ISampleProvider currentSource = new VolumeSampleProvider(source)
                        {
                            Volume = volume
                        };
                        if (currentSource.WaveFormat.Channels == 1)
                        {
                            currentSource = new MonoToStereoSampleProvider(currentSource);
                        }

                        if (currentSource.WaveFormat.SampleRate != targetSampleRate)
                        {
                            currentSource = new WdlResamplingSampleProvider(currentSource, targetSampleRate);
                        }

                        if (currentSource.WaveFormat.Channels != targetChannels)
                        {
                            if (currentSource.WaveFormat.Channels == 1)
                                currentSource = new MonoToStereoSampleProvider(currentSource);
                            else
                                currentSource = currentSource.ToStereo();
                        }

                        return currentSource;
                    }

                    var convertedMic = ConvertToStandardFormat(micReader, (float)AppSettings.Current.MicrophoneVolume);
                    var convertedSystem = ConvertToStandardFormat(systemReader, (float)AppSettings.Current.SystemVolume);
                    Debug.WriteLine($"[AudioMixer] Формат микрофона после конвертации: {convertedMic.WaveFormat}");
                    Debug.WriteLine($"[AudioMixer] Формат системы после конвертации: {convertedSystem.WaveFormat}");
                    var mixer = new MixingSampleProvider(new[] { convertedMic, convertedSystem });
                    OutputFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.mp3");
                    MediaFoundationEncoder.EncodeToMp3(mixer.ToWaveProvider(), OutputFilePath, 192000);
                    Debug.WriteLine($"[AudioMixer] MP3 файл успешно создан: {OutputFilePath}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"!!!!!! ОШИБКА В MixAndEncode: {ex.ToString()}");
                System.Windows.Application.Current.Dispatcher.Invoke(() => new NotificationWindow($"Ошибка создания MP3: {ex.Message}").Show());
            }
            finally
            {
                if (File.Exists(_tempMicPath))
                    File.Delete(_tempMicPath);
                if (File.Exists(_tempSystemPath))
                    File.Delete(_tempSystemPath);
                Debug.WriteLine("[AudioMixer] Временные WAV файлы удалены.");
            }
        }

        private void CleanupRecording()
        {
            _micWriter?.Dispose();
            _systemWriter?.Dispose();
            _microphoneSource?.Dispose();
            _systemAudioSource?.Dispose();
            _micWriter = null;
            _systemWriter = null;
            _microphoneSource = null;
            _systemAudioSource = null;
        }
    }
}
