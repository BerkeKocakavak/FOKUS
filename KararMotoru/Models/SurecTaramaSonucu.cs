namespace FokusKararMotoru.Models;

public sealed class SurecTaramaSonucu
{
    public IReadOnlyList<string> KaraListedekiSurecler { get; init; } = [];

    public string? OnPlanSurec { get; init; }

    public bool OnPlanBeyazListede { get; init; }

    public int KaraListeCezasi { get; init; }

    public string KaraListeOzeti =>
        KaraListedekiSurecler.Count == 0
            ? "Yok"
            : $"{KaraListedekiSurecler.Count} süreç ({string.Join(", ", KaraListedekiSurecler)}) [CEZA: -{KaraListeCezasi}]";
}
