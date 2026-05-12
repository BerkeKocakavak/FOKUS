using Windows.Media.Control;

namespace FokusKararMotoru.Services;

public sealed class MedyaYonetici
{
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
                GlobalSystemMediaTransportControlsSessionManager.RequestAsync()
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();

            GlobalSystemMediaTransportControlsSession? session = manager.GetCurrentSession();
            if (session is null)
            {
                return false;
            }

            return duraklat
                ? session.TryPauseAsync().AsTask().GetAwaiter().GetResult()
                : session.TryPlayAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or NotSupportedException or TypeLoadException)
        {
            return false;
        }
    }
}
