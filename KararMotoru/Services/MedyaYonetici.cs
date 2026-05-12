using Windows.Media.Control;

namespace FokusKararMotoru.Services;

public sealed class MedyaYonetici
{
    private static readonly TimeSpan KomutZamanAsimi = TimeSpan.FromMilliseconds(750);
    private bool _bizDuraklattik;

    public void OdakDurumunuUygula(bool odakDusuk)
    {
        if (odakDusuk)
        {
            Duraklat();
            return;
        }

        DevamEttir();
    }

    public void Duraklat()
    {
        if (_bizDuraklattik)
        {
            return;
        }

        if (MedyaKomutuGonder(duraklat: true))
        {
            _bizDuraklattik = true;
        }
    }

    public void DevamEttir()
    {
        if (!_bizDuraklattik)
        {
            return;
        }

        if (MedyaKomutuGonder(duraklat: false))
        {
            _bizDuraklattik = false;
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
