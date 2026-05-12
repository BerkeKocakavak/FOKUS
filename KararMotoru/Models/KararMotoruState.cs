namespace FokusKararMotoru.Models;

public sealed record KararMotoruState
{
    public DateTimeOffset Zaman { get; init; } = DateTimeOffset.Now;

    public bool PipeBagli { get; init; }

    public bool Duraklatildi { get; init; }

    public bool MudahaleAktif { get; init; }

    public string DurumMesaji { get; init; } = "Hazir";

    public BiyometrikVeri? Biyometrik { get; init; }

    public GirdiAktiviteOzeti? Girdi { get; init; }

    public SurecTaramaSonucu? Surec { get; init; }

    public OdakSonucu? Odak { get; init; }

    public string? Hata { get; init; }
}
