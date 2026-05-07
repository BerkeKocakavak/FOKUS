using System.Diagnostics;
using System.Runtime.InteropServices;
using FokusKararMotoru.Models;

namespace FokusKararMotoru.Services;

public sealed class SurecTarayici
{
    public SurecTaramaSonucu Tara(KararMotoruAyarlari ayarlar)
    {
        var karaListe = new HashSet<string>(ayarlar.KaraListe.Where(Isimli), StringComparer.OrdinalIgnoreCase);
        var beyazListe = new HashSet<string>(ayarlar.BeyazListe.Where(Isimli), StringComparer.OrdinalIgnoreCase);
        var bulunanKaraListe = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Process process in Process.GetProcesses())
        {
            try
            {
                if (!karaListe.Contains(process.ProcessName))
                {
                    continue;
                }

                if (ayarlar.SadecePencereliKaraListeSurecleri && process.MainWindowHandle == IntPtr.Zero)
                {
                    continue;
                }

                bulunanKaraListe.Add(process.ProcessName);
            }
            catch (InvalidOperationException)
            {
                // Süreç tarama sırasında kapanmış olabilir.
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Yetki gerektiren süreçler karar motorunu düşürmemeli.
            }
        }

        string? onPlanSurec = OnPlanSurecAdiAl();
        bool onPlanBeyazListede = onPlanSurec is not null && beyazListe.Contains(onPlanSurec);
        int ceza = KaraListeCezasiHesapla(bulunanKaraListe.Count, ayarlar.KaraListeCezaBasamaklari);

        if (onPlanBeyazListede && ceza > 0)
        {
            ceza = (int)Math.Round(ceza * ayarlar.BeyazListeAktifkenKaraListeCezaKatsayisi);
        }

        return new SurecTaramaSonucu
        {
            KaraListedekiSurecler = bulunanKaraListe.ToArray(),
            OnPlanSurec = onPlanSurec,
            OnPlanBeyazListede = onPlanBeyazListede,
            KaraListeCezasi = ceza
        };
    }

    private static int KaraListeCezasiHesapla(int surecSayisi, int[] basamaklar)
    {
        if (surecSayisi <= 0)
        {
            return 0;
        }

        int index = Math.Min(surecSayisi, basamaklar.Length - 1);
        return Math.Clamp(basamaklar[index], 0, 100);
    }

    private static string? OnPlanSurecAdiAl()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        IntPtr handle = GetForegroundWindow();
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        _ = GetWindowThreadProcessId(handle, out int processId);
        if (processId <= 0)
        {
            return null;
        }

        try
        {
            using Process process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static bool Isimli(string? value) => !string.IsNullOrWhiteSpace(value);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);
}
