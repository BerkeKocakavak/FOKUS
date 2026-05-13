using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FokusKararMotoru.Services;

public sealed class SurecYonetici
{
    private readonly object _syncRoot = new();
    private readonly HashSet<int> _askidakiProcessIds = new();

    private static readonly HashSet<string> KritikSurecler = new(StringComparer.OrdinalIgnoreCase)
    {
        "System",
        "Registry",
        "Idle",
        "csrss",
        "wininit",
        "winlogon",
        "smss",
        "services",
        "lsass",
        "svchost",
        "dwm",
        "fontdrvhost",
        "conhost",
        "explorer",
        "devenv",
        "KararMotoru"
    };

    [Flags]
    public enum ThreadAccess : int
    {
        SuspendResume = 0x0002
    }

    public SurecMudahaleSonucu SurecleriDondur(IEnumerable<string> karaListedekiSurecler)
    {
        int sayi = 0;
        var reddedilen = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        lock (_syncRoot)
        {
            foreach (string surecAdi in karaListedekiSurecler)
            {
                string normalizeSurecAdi = NormalizeSurecAdi(surecAdi);
                if (KritikSurecler.Contains(normalizeSurecAdi))
                {
                    reddedilen.Add(normalizeSurecAdi);
                    continue;
                }

                Process[] processes = Process.GetProcessesByName(normalizeSurecAdi);
                foreach (Process process in processes)
                {
                    SureciDondur(process);
                    sayi++;
                }
            }
        }

        return new SurecMudahaleSonucu(sayi, reddedilen.ToArray());
    }

    public SurecMudahaleSonucu SurecleriDevamEttir(IEnumerable<string> karaListedekiSurecler)
    {
        int sayi = 0;
        var reddedilen = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        lock (_syncRoot)
        {
            foreach (string surecAdi in karaListedekiSurecler)
            {
                string normalizeSurecAdi = NormalizeSurecAdi(surecAdi);
                if (KritikSurecler.Contains(normalizeSurecAdi))
                {
                    reddedilen.Add(normalizeSurecAdi);
                    continue;
                }

                Process[] processes = Process.GetProcessesByName(normalizeSurecAdi);
                foreach (Process process in processes)
                {
                    SureciDevamEttir(process);
                    sayi++;
                }
            }

            AskidakiBitmisSurecleriTemizle();
        }

        return new SurecMudahaleSonucu(sayi, reddedilen.ToArray());
    }

    public SurecMudahaleSonucu SurecleriSonlandir(IEnumerable<string> karaListedekiSurecler)
    {
        int sayi = 0;
        var reddedilen = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string surecAdi in karaListedekiSurecler)
        {
            string normalizeSurecAdi = NormalizeSurecAdi(surecAdi);
            if (KritikSurecler.Contains(normalizeSurecAdi))
            {
                reddedilen.Add(normalizeSurecAdi);
                continue;
            }

            Process[] processes = Process.GetProcessesByName(normalizeSurecAdi);
            foreach (Process process in processes)
            {
                if (process.HasExited)
                {
                    continue;
                }

                process.Kill(entireProcessTree: true);
                sayi++;
            }
        }

        return new SurecMudahaleSonucu(sayi, reddedilen.ToArray());
    }

    private static string NormalizeSurecAdi(string surecAdi)
    {
        string temiz = surecAdi.Trim();
        return temiz.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? temiz[..^4]
            : temiz;
    }

    private void SureciDondur(Process process)
    {
        try
        {
            if (process.HasExited || _askidakiProcessIds.Contains(process.Id))
            {
                return;
            }

            Kucult(process);
            foreach (ProcessThread thread in process.Threads)
            {
                IntPtr ptrOpenThread = OpenThread(ThreadAccess.SuspendResume, false, (uint)thread.Id);
                if (ptrOpenThread == IntPtr.Zero)
                {
                    continue;
                }

                try
                {
                    SuspendThread(ptrOpenThread);
                }
                finally
                {
                    CloseHandle(ptrOpenThread);
                }
            }

            _askidakiProcessIds.Add(process.Id);
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    private void SureciDevamEttir(Process process)
    {
        try
        {
            if (process.HasExited)
            {
                _askidakiProcessIds.Remove(process.Id);
                return;
            }

            bool takipliSurecVar = _askidakiProcessIds.Count > 0;
            if (takipliSurecVar && !_askidakiProcessIds.Contains(process.Id))
            {
                return;
            }

            foreach (ProcessThread thread in process.Threads)
            {
                IntPtr ptrOpenThread = OpenThread(ThreadAccess.SuspendResume, false, (uint)thread.Id);
                if (ptrOpenThread == IntPtr.Zero)
                {
                    continue;
                }

                try
                {
                    for (int deneme = 0; deneme < 16; deneme++)
                    {
                        int oncekiSayac = ResumeThread(ptrOpenThread);
                        if (oncekiSayac <= 1)
                        {
                            break;
                        }
                    }
                }
                finally
                {
                    CloseHandle(ptrOpenThread);
                }
            }

            _askidakiProcessIds.Remove(process.Id);
        }
        catch (InvalidOperationException)
        {
            _askidakiProcessIds.Remove(process.Id);
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    private void AskidakiBitmisSurecleriTemizle()
    {
        if (_askidakiProcessIds.Count == 0)
        {
            return;
        }

        foreach (int processId in _askidakiProcessIds.ToArray())
        {
            try
            {
                using Process process = Process.GetProcessById(processId);
                if (process.HasExited)
                {
                    _askidakiProcessIds.Remove(processId);
                }
            }
            catch (ArgumentException)
            {
                _askidakiProcessIds.Remove(processId);
            }
            catch (InvalidOperationException)
            {
                _askidakiProcessIds.Remove(processId);
            }
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenThread(ThreadAccess dwDesiredAccess, bool bInheritHandle, uint dwThreadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SuspendThread(IntPtr hThread);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int ResumeThread(IntPtr hThread);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    private static void Kucult(Process process)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            if (process.MainWindowHandle != IntPtr.Zero)
            {
                ShowWindow(process.MainWindowHandle, ShowMinimized);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private const int ShowMinimized = 6;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}

public sealed record SurecMudahaleSonucu(
    int EtkilenenSurecSayisi,
    IReadOnlyList<string> ReddedilenKritikSurecler)
{
    public bool KritikSurecReddedildi => ReddedilenKritikSurecler.Count > 0;
}
