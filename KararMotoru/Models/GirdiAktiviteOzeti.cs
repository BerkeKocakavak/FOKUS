namespace FokusKararMotoru.Models;

public sealed class GirdiAktiviteOzeti
{
    public DateTimeOffset Zaman { get; init; } = DateTimeOffset.Now;

    public int TusVurusu { get; init; }

    public int FareHareketi { get; init; }

    public double FareMesafesi { get; init; }

    public double HareketsizSaniye { get; init; }

    public double TusDakika { get; init; }

    public double FarePikselDakika { get; init; }
}
