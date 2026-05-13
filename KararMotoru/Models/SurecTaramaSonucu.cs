namespace FokusKararMotoru.Models;

public sealed class SurecTaramaSonucu
{
    public IReadOnlyList<string> KaraListedekiSurecler { get; init; } = [];

    public string? OnPlanSurec { get; init; }

    public bool OnPlanBeyazListede { get; init; }
}
