using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FokusKararMotoru.Services;

public sealed class SurecYonetici
{
    private readonly object _syncRoot = new();
    private readonly HashSet<int> _askidakiProcessIds = new();

    private static readonly HashSet<string> KritikSurecler = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "csrss", "wininit", "smss", "services", "lsass", "svchost", "devenv", "KararMotoru"
    };

    [Flags]
    public enum ThreadAccess : int
    {
        SuspendResume = 0x0002
    }

    public void SurecleriDondur(IEnumerable<string> karaListedekiSurecler)
    {
        lock (_syncRoot)
        {
            foreach (string surecAdi in karaListedekiSurecler)
            {
                if (KritikSurecler.Contains(surecAdi))
                {
                    continue;
                }

                Process[] processes = Process.GetProcessesByName(surecAdi);
                foreach (Process process in processes)
                {
                    SureciDondur(process);
                }
            }
        }
    }

    public void SurecleriDevamEttir(IEnumerable<string> karaListedekiSurecler)
    {
        lock (_syncRoot)
        {
            foreach (string surecAdi in karaListedekiSurecler)
            {
                if (KritikSurecler.Contains(surecAdi))
                {
                    continue;
                }

                Process[] processes = Process.GetProcessesByName(surecAdi);
                foreach (Process process in processes)
                {
                    SureciDevamEttir(process);
                }
            }

            AskidakiBitmisSurecleriTemizle();
        }
    }

    public int SurecleriSonlandir(IEnumerable<string> karaListedekiSurecler)
    {
        int sayi = 0;
        foreach (string surecAdi in karaListedekiSurecler)
        {
            if (KritikSurecler.Contains(surecAdi))
            {
                continue;
            }

            Process[] processes = Process.GetProcessesByName(surecAdi);
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

        return sayi;
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
