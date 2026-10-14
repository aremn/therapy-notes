using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AudioRecorderTrayApp.Models;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AudioRecorderTrayApp.Services
{
    public class WordTimestamp
    {
        public string word { get; set; } = "";
        public double start { get; set; }
        public double end { get; set; }
    }

    public class GigaAmResult
    {
        public bool success { get; set; }
        public string? transcription { get; set; }
        public List<WordTimestamp>? word_timestamps { get; set; }
        public string? error { get; set; }
    }

    public static class TranscriptionService
    {
        public static Task<List<SpeakerSegment>> TranscribeFileToSegmentsAsyncAdmin(string filePath, string speakerLabel, CancellationToken ct = default) => TranscribeFileToSegmentsAsync(filePath, speakerLabel, ct);
        public static async Task<List<SpeakerSegment>> TranscribeFileToSegmentsAsync(string filePath, string speakerLabel, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                throw new FileNotFoundException("Audio file not found.", filePath);
            var swAll = Stopwatch.StartNew();
            Logger.Info($"[Transcribe GigaAM] start speaker={speakerLabel} file='{filePath}'");
            using var preparedAudioStream = PrepareWaveForModel(filePath);
            var tempWavPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.wav");
            await using (var fileStream = new FileStream(tempWavPath, FileMode.Create, FileAccess.Write))
            {
                await preparedAudioStream.CopyToAsync(fileStream, ct);
            }

            Logger.Info($"[Transcribe GigaAM] Prepared audio saved to '{tempWavPath}'");
            string stdout;
            try
            {
                Logger.Info($"[Transcribe GigaAM] Using on-demand GigaAM service");
                stdout = await GigaAmRunner.TranscribeAsync(tempWavPath, ct);
                Logger.Info($"[Transcribe GigaAM] On-demand service returned: {stdout}");
            }
            finally
            {
                File.Delete(tempWavPath);
            }

            if (string.IsNullOrWhiteSpace(stdout))
            {
                Logger.Error($"[Transcribe GigaAM] Transcription failed - empty output");
                throw new Exception("Transcription failed: empty output");
            }

            Logger.Info($"[Transcribe GigaAM] Raw stdout: {stdout}");
            try
            {
                var parsed = JsonSerializer.Deserialize<GigaAmResult>(stdout);
                if (parsed == null || !parsed.success)
                {
                    var err = parsed?.error ?? "unknown";
                    Logger.Error($"[Transcribe GigaAM] Engine returned an error: {err}");
                    Logger.Error($"[Transcribe GigaAM] Parsed object: success={parsed?.success}, error={parsed?.error}");
                    Logger.Error($"[Transcribe GigaAM] Full JSON: {stdout}");
                    throw new Exception($"Transcription failed: {err}");
                }

                var segments = AggregateWordsToSegments(parsed.word_timestamps ?? new List<WordTimestamp>(), speakerLabel);
                swAll.Stop();
                Logger.Info($"[Transcribe GigaAM] done via on-demand service speaker={speakerLabel} segments={segments.Count} elapsed_ms={swAll.ElapsedMilliseconds}");
                return segments;
            }
            catch (JsonException jsonEx)
            {
                Logger.Error($"[Transcribe GigaAM] Failed to parse JSON response: {jsonEx.Message}. Response: {stdout}");
                throw;
            }
        }

        private static List<SpeakerSegment> AggregateWordsToSegments(List<WordTimestamp> words, string speakerLabel, double maxGapSeconds = 1.0)
        {
            if (words == null || !words.Any())
                return new List<SpeakerSegment>();
            var segments = new List<SpeakerSegment>();
            var currentSegment = new SpeakerSegment
            {
                Speaker = speakerLabel,
                Text = words[0].word,
                Start = words[0].start,
                End = words[0].end
            };
            for (int i = 1; i < words.Count; i++)
            {
                var currentWord = words[i];
                var previousWord = words[i - 1];
                if (currentWord.start - previousWord.end <= maxGapSeconds)
                {
                    currentSegment.Text += " " + currentWord.word;
                    currentSegment.End = currentWord.end;
                }
                else
                {
                    segments.Add(currentSegment);
                    currentSegment = new SpeakerSegment
                    {
                        Speaker = speakerLabel,
                        Text = currentWord.word,
                        Start = currentWord.start,
                        End = currentWord.end
                    };
                }
            }

            segments.Add(currentSegment);
            return segments;
        }

        private static Stream PrepareWaveForModel(string path)
        {
            ISampleProvider sampleProvider;
            IDisposable? readerToDispose = null;
            try
            {
                var afr = new AudioFileReader(path);
                sampleProvider = afr;
                readerToDispose = afr;
            }
            catch
            {
                var wfr = new WaveFileReader(path);
                sampleProvider = wfr.ToSampleProvider();
                readerToDispose = wfr;
            }

            if (sampleProvider.WaveFormat.SampleRate != 16000)
            {
                sampleProvider = new WdlResamplingSampleProvider(sampleProvider, 16000);
            }

            if (sampleProvider.WaveFormat.Channels != 1)
            {
                sampleProvider = sampleProvider.ToMono();
            }

            var ms = new MemoryStream();
            WaveFileWriter.WriteWavFileToStream(ms, sampleProvider.ToWaveProvider16());
            ms.Position = 0;
            readerToDispose?.Dispose();
            return ms;
        }
    }
}
