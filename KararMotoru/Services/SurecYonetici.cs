using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FokusKararMotoru.Services;

public sealed class SurecYonetici
{
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
        foreach (string surecAdi in karaListedekiSurecler)
        {
            if (KritikSurecler.Contains(surecAdi))
            {
                continue;
            }

            Process[] processes = Process.GetProcessesByName(surecAdi);
            foreach (Process process in processes)
            {
                Kucult(process);
                foreach (ProcessThread thread in process.Threads)
                {
                    IntPtr ptrOpenThread = OpenThread(ThreadAccess.SuspendResume, false, (uint)thread.Id);
                    if (ptrOpenThread != IntPtr.Zero)
                    {
                        SuspendThread(ptrOpenThread);
                        CloseHandle(ptrOpenThread);
                    }
                }
            }
        }
    }

    public void SurecleriDevamEttir(IEnumerable<string> karaListedekiSurecler)
    {
        foreach (string surecAdi in karaListedekiSurecler)
        {
            Process[] processes = Process.GetProcessesByName(surecAdi);
            foreach (Process process in processes)
            {
                foreach (ProcessThread thread in process.Threads)
                {
                    IntPtr ptrOpenThread = OpenThread(ThreadAccess.SuspendResume, false, (uint)thread.Id);
                    if (ptrOpenThread != IntPtr.Zero)
                    {
                        ResumeThread(ptrOpenThread);
                        CloseHandle(ptrOpenThread);
                    }
                }
            }
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
