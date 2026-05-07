using System.Runtime.InteropServices;
using FokusKararMotoru.Models;

namespace FokusKararMotoru.Services;

public sealed class GirdiIzleyici : IDisposable
{
    private readonly object _kilit = new();
    private readonly Timer _timer;
    private readonly bool[] _oncekiTusDurumlari = new bool[256];
    private readonly Queue<DateTimeOffset> _tusOlaylari = new();
    private readonly Queue<(DateTimeOffset Zaman, double Mesafe)> _fareOlaylari = new();
    private DateTimeOffset _sonAktivite = DateTimeOffset.Now;
    private Point? _oncekiFareKonumu;
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
            EskiOlaylariTemizle(baslangic);
            double fareMesafesi = _fareOlaylari.Sum(olay => olay.Mesafe);
            double dakikaCarpani = 60.0 / Math.Max(1, pencereSaniye);

            return new GirdiAktiviteOzeti
            {
                Zaman = simdi,
                TusVurusu = _tusOlaylari.Count,
                FareHareketi = _fareOlaylari.Count,
                FareMesafesi = fareMesafesi,
                HareketsizSaniye = Math.Max(0, (simdi - _sonAktivite).TotalSeconds),
                TusDakika = _tusOlaylari.Count * dakikaCarpani,
                FarePikselDakika = fareMesafesi * dakikaCarpani
            };
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
            for (int tus = 8; tus < _oncekiTusDurumlari.Length; tus++)
            {
                bool basili = (GetAsyncKeyState(tus) & 0x8000) != 0;
                if (basili && !_oncekiTusDurumlari[tus])
                {
                    _tusOlaylari.Enqueue(simdi);
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
                        _sonAktivite = simdi;
                    }
                }

                _oncekiFareKonumu = konum;
            }
        }
    }

    private void EskiOlaylariTemizle(DateTimeOffset baslangic)
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
