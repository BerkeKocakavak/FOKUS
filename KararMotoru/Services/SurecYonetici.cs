using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FokusKararMotoru.Services
{
    public class SurecYonetici
    {
        // 1. Windows API Fonksiyonlar� (P/Invoke)
        [Flags]
        public enum ThreadAccess : int
        {
            SUSPEND_RESUME = 0x0002 // Access Denied yememek i�in sadece bu yetkiyi istiyoruz
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenThread(ThreadAccess dwDesiredAccess, bool bInheritHandle, uint dwThreadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern uint SuspendThread(IntPtr hThread);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern int ResumeThread(IntPtr hThread);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr handle);

        // 2. Sistemin ��kmesini Engelleyen Hardcoded Beyaz Liste
        private static readonly HashSet<string> KritikSurecler = new(StringComparer.OrdinalIgnoreCase)
        {
            "explorer", "csrss", "wininit", "smss", "services", "lsass", "svchost", "devenv", "KararMotoru"
        };

        // 3. S�re�leri Dondurma Metodu
        public void SurecleriDondur(IEnumerable<string> karaListedekiSurecler)
        {
            foreach (string surecAdi in karaListedekiSurecler)
            {
                if (KritikSurecler.Contains(surecAdi)) continue; // G�venlik kalkan�

                Process[] processes = Process.GetProcessesByName(surecAdi);
                foreach (Process process in processes)
                {
                    foreach (ProcessThread thread in process.Threads)
                    {
                        IntPtr ptrOpenThread = OpenThread(ThreadAccess.SUSPEND_RESUME, false, (uint)thread.Id);
                        if (ptrOpenThread != IntPtr.Zero)
                        {
                            SuspendThread(ptrOpenThread);
                            CloseHandle(ptrOpenThread);
                        }
                    }
                }
            }
        }

        // 4. S�re�leri Serbest B�rakma (Devam Ettirme) Metodu
        public void SurecleriDevamEttir(IEnumerable<string> karaListedekiSurecler)
        {
            foreach (string surecAdi in karaListedekiSurecler)
            {
                Process[] processes = Process.GetProcessesByName(surecAdi);
                foreach (Process process in processes)
                {
                    foreach (ProcessThread thread in process.Threads)
                    {
                        IntPtr ptrOpenThread = OpenThread(ThreadAccess.SUSPEND_RESUME, false, (uint)thread.Id);
                        if (ptrOpenThread != IntPtr.Zero)
                        {
                            ResumeThread(ptrOpenThread);
                            CloseHandle(ptrOpenThread);
                        }
                    }
                }
            }
        }
    }
}