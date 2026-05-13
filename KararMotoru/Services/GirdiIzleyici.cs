using System.Runtime.InteropServices;
using FokusKararMotoru.Models;

namespace FokusKararMotoru.Services;

public sealed class GirdiIzleyici : IDisposable
{
    private readonly object _kilit = new();
    private readonly Timer _timer;
    private readonly DateTimeOffset _oturumBaslangic = DateTimeOffset.Now;
    private readonly bool[] _oncekiTusDurumlari = new bool[256];
    private readonly Queue<DateTimeOffset> _tusOlaylari = new();
    private readonly Queue<(DateTimeOffset Zaman, double Mesafe)> _fareOlaylari = new();
    private DateTimeOffset _sonAktivite = DateTimeOffset.Now;
    private DateTimeOffset? _duraklatmaBaslangic;
    private Point? _oncekiFareKonumu;
    private int _toplamTusVurusu;
    private double _toplamFareMesafesi;
    private TimeSpan _duraklatilanSure = TimeSpan.Zero;
    private bool _duraklatildi;
    private bool _disposed;

    public GirdiIzleyici(int orneklemeMs)
    {
        _timer = new Timer(_ => Ornekle(), null, dueTime: 0, period: orneklemeMs);
    }

    public GirdiAktiviteOzeti OzetAl(int pencereSaniye)
    {
        DateTimeOffset simdi = DateTimeOffset.Now;
        DateTimeOffset baslangic = simdi.AddSeconds(-pencereSaniye);

        lock (_kilit)
        {
            PencereDisiOlaylariTemizle(baslangic);
            double fareMesafesi = _fareOlaylari.Sum(olay => olay.Mesafe);
            double oturumDakika = Math.Max(1.0 / 60.0, AktifOturumSuresi(simdi).TotalMinutes);

            return new GirdiAktiviteOzeti
            {
                Zaman = simdi,
                TusVurusu = _tusOlaylari.Count,
                FareHareketi = _fareOlaylari.Count,
                FareMesafesi = fareMesafesi,
                HareketsizSaniye = Math.Max(0, (simdi - _sonAktivite).TotalSeconds),
                TusDakika = _toplamTusVurusu / oturumDakika,
                FarePikselDakika = _toplamFareMesafesi / oturumDakika
            };
        }
    }

    public void DuraklatmaDurumuAyarla(bool aktif)
    {
        DateTimeOffset simdi = DateTimeOffset.Now;
        lock (_kilit)
        {
            if (_duraklatildi == aktif)
            {
                return;
            }

            _duraklatildi = aktif;
            if (aktif)
            {
                _duraklatmaBaslangic = simdi;
            }
            else if (_duraklatmaBaslangic is DateTimeOffset baslangic)
            {
                _duraklatilanSure += simdi - baslangic;
                _duraklatmaBaslangic = null;
                _sonAktivite = simdi;
                Array.Clear(_oncekiTusDurumlari);
                _oncekiFareKonumu = null;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _timer.Dispose();
        _disposed = true;
    }

    private void Ornekle()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        DateTimeOffset simdi = DateTimeOffset.Now;
        lock (_kilit)
        {
            if (_duraklatildi)
            {
                Array.Clear(_oncekiTusDurumlari);
                _oncekiFareKonumu = null;
                return;
            }

            for (int tus = 8; tus < _oncekiTusDurumlari.Length; tus++)
            {
                short durum = GetAsyncKeyState(tus);
                bool basili = (durum & 0x8000) != 0;
                bool yeniBasma = (durum & 0x0001) != 0 || (basili && !_oncekiTusDurumlari[tus]);
                if (yeniBasma)
                {
                    _tusOlaylari.Enqueue(simdi);
                    _toplamTusVurusu++;
                    _sonAktivite = simdi;
                }

                _oncekiTusDurumlari[tus] = basili;
            }

            if (GetCursorPos(out Point konum))
            {
                if (_oncekiFareKonumu is Point onceki)
                {
                    double mesafe = Math.Sqrt(Math.Pow(konum.X - onceki.X, 2) + Math.Pow(konum.Y - onceki.Y, 2));
                    if (mesafe >= 2)
                    {
                        _fareOlaylari.Enqueue((simdi, mesafe));
                        _toplamFareMesafesi += mesafe;
                        _sonAktivite = simdi;
                    }
                }

                _oncekiFareKonumu = konum;
            }
        }
    }

    private TimeSpan AktifOturumSuresi(DateTimeOffset simdi)
    {
        TimeSpan duraklama = _duraklatilanSure;
        if (_duraklatmaBaslangic is DateTimeOffset baslangic)
        {
            duraklama += simdi - baslangic;
        }

        TimeSpan sure = simdi - _oturumBaslangic - duraklama;
        return sure > TimeSpan.Zero ? sure : TimeSpan.Zero;
    }

    private void PencereDisiOlaylariTemizle(DateTimeOffset baslangic)
    {
        while (_tusOlaylari.Count > 0 && _tusOlaylari.Peek() < baslangic)
        {
            _tusOlaylari.Dequeue();
        }

        while (_fareOlaylari.Count > 0 && _fareOlaylari.Peek().Zaman < baslangic)
        {
            _fareOlaylari.Dequeue();
        }
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;

        public int Y;
    }
}
