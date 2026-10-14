using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AudioRecorderTrayApp.Services;

namespace AudioRecorderTrayApp.Core
{
    public static class TextEnhancementService
    {
        public static async Task<string> EnhanceAsync(string text, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;
            if (!AppSettings.Current.UseTextEnhancement)
                return text;
            try
            {
                var sw = Stopwatch.StartNew();
                Logger.Info($"[Enhancer] EnhanceAsync: begin len={text.Length}");
                var result = await TryEnhanceWithGeminiAsync(text, ct);
                if (!string.IsNullOrWhiteSpace(result))
                {
                    sw.Stop();
                    Logger.Info($"[Enhancer] EnhanceAsync: success elapsedMs={sw.ElapsedMilliseconds}");
                    return result;
                }

                Logger.Warn("[Enhancer] Fallback: returning original text (enhancement failed).");
                return text;
            }
            catch (Exception ex)
            {
                Logger.Error($"[Enhancer] ERROR EnhanceAsync: {ex}");
                return text;
            }
            finally
            {
                Logger.Info("[Enhancer] EnhanceAsync: end");
            }
        }

        private static (string exe, bool useLauncher) ResolvePython()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(AppSettings.Current.PythonPath) && File.Exists(AppSettings.Current.PythonPath))
                {
                    Logger.Info($"[Enhancer] ResolvePython: using AppSettings path '{AppSettings.Current.PythonPath}'");
                    return (AppSettings.Current.PythonPath!, false);
                }
            }
            catch
            {
            }

            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var projVenv = Path.GetFullPath(Path.Combine(baseDir, "..", "..", ".venv", "Scripts", "python.exe"));
                if (File.Exists(projVenv))
                {
                    Logger.Info($"[Enhancer] ResolvePython: using project venv '{projVenv}'");
                    return (projVenv, false);
                }

                var localVenv = Path.Combine(baseDir, ".venv", "Scripts", "python.exe");
                if (File.Exists(localVenv))
                {
                    Logger.Info($"[Enhancer] ResolvePython: using local venv '{localVenv}'");
                    return (localVenv, false);
                }
            }
            catch
            {
            }

            Logger.Info("[Enhancer] ResolvePython: falling back to Windows launcher 'py' or 'python' in PATH");
            return ("py", true);
        }

        private static string? ResolveScriptPath()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var projectRoot = Path.GetFullPath(Path.Combine(baseDir, "..", ".."));
                var configured = string.IsNullOrWhiteSpace(AppSettings.Current.EnhancerScript) ? "enhancer.py" : AppSettings.Current.EnhancerScript!;
                if (Path.IsPathRooted(configured))
                {
                    Logger.Info($"[Enhancer] ResolveScriptPath: absolute '{configured}'");
                    return configured;
                }

                var candidate0 = Path.Combine(projectRoot, configured);
                if (File.Exists(candidate0))
                {
                    Logger.Info($"[Enhancer] ResolveScriptPath: project root '{candidate0}'");
                    return candidate0;
                }

                var candidate1 = Path.Combine(baseDir, configured);
                if (File.Exists(candidate1))
                {
                    Logger.Info($"[Enhancer] ResolveScriptPath: '{candidate1}'");
                    return candidate1;
                }

                var candidate2 = Path.Combine(baseDir, "Configuration", "enhancer.py");
                if (File.Exists(candidate2))
                {
                    Logger.Info($"[Enhancer] ResolveScriptPath: fallback '{candidate2}'");
                    return candidate2;
                }

                var candidate3 = Path.Combine(baseDir, "enhancer.py");
                Logger.Info($"[Enhancer] ResolveScriptPath: last '{candidate3}'");
                return candidate3;
            }
            catch
            {
                return null;
            }
        }

        private static async Task<string?> TryEnhanceWithGeminiAsync(string text, CancellationToken ct)
        {
            var(python, useLauncher) = ResolvePython();
            var scriptPath = ResolveScriptPath();
            if (string.IsNullOrEmpty(scriptPath) || !File.Exists(scriptPath))
            {
                Logger.Warn($"[Enhancer] Script not found: '{scriptPath}'");
                return null;
            }

            Logger.Info($"[Enhancer] Using python: {python} (launcher={useLauncher})");
            Logger.Info($"[Enhancer] Using script: {scriptPath}");
            try
            {
                if (AppSettings.Current.VerboseTranscriberLogging)
                {
                    Logger.Info("[Enhancer] INPUT FULL (before enhancement) >>>\n" + (text ?? string.Empty) + "\n<<< [END ENHANCER INPUT]");
                }
                else
                {
                    var prev = text?.Length > 300 ? (text.Substring(0, 300) + "…") : (text ?? string.Empty);
                    prev = prev.Replace("\r", " ").Replace("\n", " ");
                    Logger.Info($"[Enhancer] INPUT preview len={text?.Length ?? 0}: \"{prev}\"");
                }
            }
            catch
            {
            }

            string? tempInputFile = null;
            try
            {
                tempInputFile = Path.GetTempFileName();
                await File.WriteAllTextAsync(tempInputFile, text, Encoding.UTF8, ct);
                Logger.Info($"[Enhancer] Created temp input file: {tempInputFile} (size: {text?.Length ?? 0} chars)");
            }
            catch (Exception ex)
            {
                Logger.Error($"[Enhancer] Failed to create temp input file: {ex}");
                return null;
            }

            var arguments = (useLauncher ? "-3 " : string.Empty) + $"-X utf8 \"{scriptPath}\" \"{tempInputFile}\"";
            var psi = new ProcessStartInfo
            {
                FileName = python,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardInput = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            psi.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;
            psi.Environment["PYTHONIOENCODING"] = "utf-8";
            psi.Environment["PYTHONUTF8"] = "1";
            psi.Environment["ENHANCER_VERBOSE"] = AppSettings.Current.VerboseTranscriberLogging ? "1" : "0";
            var cfgDir = Path.Combine(psi.WorkingDirectory, "Configuration");
            Logger.Info($"[Enhancer] WorkDir={psi.WorkingDirectory} ConfigDir={(Directory.Exists(cfgDir) ? cfgDir : "<none>")}");
            try
            {
                using var proc = new Process
                {
                    StartInfo = psi,
                    EnableRaisingEvents = true
                };
                var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                proc.Exited += (s, e) => tcs.TrySetResult(proc.ExitCode);
                var sw = Stopwatch.StartNew();
                if (AppSettings.Current.VerboseTranscriberLogging)
                {
                    var prev = (text?.Length ?? 0) > 200 ? text!.Substring(0, 200) + "…" : (text ?? string.Empty);
                    prev = prev.Replace("\r", " ").Replace("\n", " ");
                    Logger.Info($"[Enhancer] input_len={text?.Length ?? 0}, preview=\"{prev}\"");
                }

                if (!proc.Start())
                {
                    Logger.Error("[Enhancer] Failed to start python process.");
                    return null;
                }

                var stdoutTask = proc.StandardOutput.ReadToEndAsync();
                var stderrTask = proc.StandardError.ReadToEndAsync();
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
                linked.CancelAfter(TimeSpan.FromSeconds(60));
                try
                {
                    await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, linked.Token));
                }
                catch (OperationCanceledException)
                {
                }

                var readSw = Stopwatch.StartNew();
                string stdout = string.Empty;
                string stderr = string.Empty;
                try
                {
                    stdout = await stdoutTask;
                }
                catch
                {
                }

                try
                {
                    stderr = await stderrTask;
                }
                catch
                {
                }

                readSw.Stop();
                Logger.Info($"[Enhancer] read_pipes_ms={readSw.ElapsedMilliseconds}");
                if (!proc.HasExited)
                {
                    try
                    {
                        proc.Kill(true);
                    }
                    catch
                    {
                    }

                    Logger.Warn("[Enhancer] Killed by timeout.");
                    return null;
                }

                sw.Stop();
                Logger.Info($"[Enhancer] ExitCode={proc.ExitCode}, Elapsed={sw.ElapsedMilliseconds}ms, stdout_len={stdout?.Length ?? 0}, stderr_len={stderr?.Length ?? 0}");
                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    if (AppSettings.Current.VerboseTranscriberLogging)
                    {
                        var serr = stderr.Length > 4000 ? stderr.Substring(0, 4000) + "…" : stderr;
                        Logger.Info($"[Enhancer][stderr] {serr.Trim()} ");
                    }
                    else
                    {
                        var serrPrev = stderr.Length > 400 ? stderr.Substring(0, 400) + "…" : stderr;
                        serrPrev = serrPrev.Replace("\r", " ").Replace("\n", " ");
                        Logger.Info($"[Enhancer][stderr_prev] {serrPrev}");
                    }
                }

                if (proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
                {
                    var cleaned = stdout.Replace("\uFFFD", string.Empty).Trim();
                    try
                    {
                        if (AppSettings.Current.VerboseTranscriberLogging)
                        {
                            Logger.Info("[Enhancer] OUTPUT FULL (after enhancement) >>>\n" + cleaned + "\n<<< [END ENHANCER OUTPUT]");
                        }
                        else
                        {
                            var outPrev = cleaned.Length > 300 ? cleaned.Substring(0, 300) + "…" : cleaned;
                            outPrev = outPrev.Replace("\r", " ").Replace("\n", " ");
                            Logger.Info($"[Enhancer] output_len={cleaned.Length}, preview=\"{outPrev}\"");
                        }
                    }
                    catch
                    {
                    }

                    return cleaned;
                }

                return null;
            }
            finally
            {
                if (tempInputFile != null && File.Exists(tempInputFile))
                {
                    try
                    {
                        File.Delete(tempInputFile);
                        Logger.Info($"[Enhancer] Deleted temp input file: {tempInputFile}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"[Enhancer] Failed to delete temp input file {tempInputFile}: {ex}");
                    }
                }
            }
        }
    }
}
