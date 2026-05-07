namespace FokusKararMotoru.Models;

public sealed class OdakSonucu
{
    public int Puan { get; init; }

    public double HamHedefPuan { get; init; }

    public bool MudahaleGerekli { get; init; }

    public IReadOnlyList<CezaKalemi> Cezalar { get; init; } = [];

    public string CezaOzeti =>
        Cezalar.Count == 0
            ? "Yok"
            : string.Join(", ", Cezalar.Select(ceza => $"{ceza.Kaynak} -{ceza.Deger:0.#}"));
}
