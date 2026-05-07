using System.Diagnostics;
using System.IO;
using System.Text;

namespace FokusKararMotoru.Services;

public sealed class PythonCameraWorker : IDisposable
{
    private readonly string _projeKoku;
    private Process? _process;
    private StreamWriter? _logWriter;
    private bool _disposed;

    public PythonCameraWorker(string projeKoku)
    {
        _projeKoku = projeKoku;
        PipeName = $"fokus_pipe_{Environment.ProcessId}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        FramePath = Path.Combine(_projeKoku, "camera_frame.jpg");
        LogPath = Path.Combine(_projeKoku, "camera_worker.log");
    }

    public string PipeName { get; }

    public string FramePath { get; }

    public string LogPath { get; }

    public int PreviewFps { get; set; } = 30;

    public int AnalysisFps { get; set; } = 10;

    public bool Calisiyor => _process is { HasExited: false };

    public event EventHandler<string>? LogChanged;

    public void Start()
    {
        if (Calisiyor)
        {
            return;
        }

        string scriptPath = Path.Combine(_projeKoku, "kamera_test.py");
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException("Kamera betigi bulunamadi.", scriptPath);
        }

        _logWriter?.Dispose();
        _logWriter = new StreamWriter(LogPath, false, Encoding.UTF8) { AutoFlush = true };

        var startInfo = new ProcessStartInfo
        {
            FileName = PythonKomutuBul(),
            WorkingDirectory = _projeKoku,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("--headless");
        startInfo.ArgumentList.Add("--frame-output");
        startInfo.ArgumentList.Add(FramePath);
        startInfo.ArgumentList.Add("--pipe-name");
        startInfo.ArgumentList.Add(PipeName);
        startInfo.ArgumentList.Add("--preview-fps");
        startInfo.ArgumentList.Add(Math.Clamp(PreviewFps, 5, 60).ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--analysis-fps");
        startInfo.ArgumentList.Add(Math.Clamp(AnalysisFps, 1, 30).ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.Environment["FOKUS_KARAR_MOTORU_OTOMATIK"] = "0";
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        startInfo.Environment["PYTHONUNBUFFERED"] = "1";

        _process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };
        _process.OutputDataReceived += (_, e) => YazLog(e.Data);
        _process.ErrorDataReceived += (_, e) => YazLog(e.Data);
        _process.Exited += (_, _) => YazLog("Python kamera isçisi kapandi.");

        if (!_process.Start())
        {
            throw new InvalidOperationException("Python kamera isçisi baslatilamadi.");
        }

        YazLog("Python kamera isçisi baslatildi.");
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public async Task<PythonDependencyCheckResult> CheckDependenciesAsync(bool fast = true, TimeSpan? timeout = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = PythonKomutuBul(),
            WorkingDirectory = _projeKoku,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.ArgumentList.Add(Path.Combine(_projeKoku, "bagimlilik_kontrol.py"));
        if (fast)
        {
            startInfo.ArgumentList.Add("--fast");
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new PythonDependencyCheckResult(false, "Python baslatilamadi.");
            }

            using var timeoutSource = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(20));
            string output = await process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
            string error = await process.StandardError.ReadToEndAsync(timeoutSource.Token);
            await process.WaitForExitAsync(timeoutSource.Token);
            string sonuc = (output + Environment.NewLine + error).Trim();
            if (string.IsNullOrWhiteSpace(sonuc))
            {
                sonuc = "Bagimlilik kontrolu tamamlandi.";
            }

            return new PythonDependencyCheckResult(process.ExitCode == 0, sonuc);
        }
        catch (OperationCanceledException)
        {
            return new PythonDependencyCheckResult(false, "Bagimlilik kontrolu zaman asimina ugradi.");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new PythonDependencyCheckResult(false, "Bagimlilik kontrolu calistirilamadi: " + ex.Message);
        }
    }

    public async Task StopAsync(TimeSpan? timeout = null)
    {
        Process? process = _process;
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                using var timeoutSource = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(2));
                try
                {
                    await process.WaitForExitAsync(timeoutSource.Token);
                }
                catch (OperationCanceledException)
                {
                    YazLog("Python kamera işçisi kapanış zaman aşımına uğradı.");
                }
            }
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            process.Dispose();
            _process = null;
            _logWriter?.Dispose();
            _logWriter = null;
        }
    }

    public void Stop()
    {
        StopAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _disposed = true;
    }

    private void YazLog(string? satir)
    {
        if (string.IsNullOrWhiteSpace(satir))
        {
            return;
        }

        string mesaj = $"[{DateTime.Now:HH:mm:ss}] {satir}";
        try
        {
            _logWriter?.WriteLine(mesaj);
        }
        catch (ObjectDisposedException)
        {
        }

        LogChanged?.Invoke(this, mesaj);
    }

    private static string PythonKomutuBul()
    {
        string? envPython = Environment.GetEnvironmentVariable("PYTHON");
        if (!string.IsNullOrWhiteSpace(envPython))
        {
            return envPython;
        }

        return "python";
    }
}

public sealed record PythonDependencyCheckResult(bool Ok, string Message);
