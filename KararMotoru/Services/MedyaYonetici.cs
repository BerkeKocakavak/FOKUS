using Windows.Media.Control;

namespace FokusKararMotoru.Services;

public sealed class MedyaYonetici
{
    private static readonly TimeSpan KomutZamanAsimi = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan KomutTekrarAraligi = TimeSpan.FromSeconds(2);
    private readonly object _syncRoot = new();
    private DateTimeOffset _sonKomutDenemesi = DateTimeOffset.MinValue;
    private bool _bizDuraklattik;
    private bool _hedefDuraklatildi;
    private bool _komutCalisiyor;

    public void OdakDurumunuUygula(bool odakDusuk)
    {
        MedyaDurumunuPlanla(odakDusuk);
    }

    public void Duraklat()
    {
        MedyaDurumunuPlanla(true);
    }

    public void DevamEttir()
    {
        MedyaDurumunuPlanla(false);
    }

    private void MedyaDurumunuPlanla(bool duraklat)
    {
        lock (_syncRoot)
        {
            _hedefDuraklatildi = duraklat;
            DateTimeOffset simdi = DateTimeOffset.Now;
            if (_komutCalisiyor || _bizDuraklattik == _hedefDuraklatildi)
            {
                return;
            }

            if (simdi - _sonKomutDenemesi < KomutTekrarAraligi)
            {
                return;
            }

            _sonKomutDenemesi = simdi;
            _komutCalisiyor = true;
        }

        _ = Task.Run(KomutDongusu);
    }

    private void KomutDongusu()
    {
        while (true)
        {
            bool hedef;
            lock (_syncRoot)
            {
                hedef = _hedefDuraklatildi;
            }

            bool basarili = MedyaKomutuGonder(hedef);

            lock (_syncRoot)
            {
                if (basarili)
                {
                    _bizDuraklattik = hedef;
                }

                if (_bizDuraklattik == _hedefDuraklatildi || !basarili)
                {
                    _komutCalisiyor = false;
                    return;
                }
            }
        }
    }

    private static bool MedyaKomutuGonder(bool duraklat)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            return false;
        }

        try
        {
            GlobalSystemMediaTransportControlsSessionManager manager =
                ZamanSinirliBekle(GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask());

            GlobalSystemMediaTransportControlsSession? session = manager.GetCurrentSession();
            if (session is null)
            {
                return false;
            }

            return duraklat
                ? ZamanSinirliBekle(session.TryPauseAsync().AsTask())
                : ZamanSinirliBekle(session.TryPlayAsync().AsTask());
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or NotSupportedException or TypeLoadException or TimeoutException or AggregateException)
        {
            return false;
        }
    }

    private static T ZamanSinirliBekle<T>(Task<T> task)
    {
        if (!task.Wait(KomutZamanAsimi))
        {
            throw new TimeoutException();
        }

        return task.GetAwaiter().GetResult();
    }
}
