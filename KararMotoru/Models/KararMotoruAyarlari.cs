namespace FokusKararMotoru.Models;

public sealed class KararMotoruAyarlari
{
    public int OdakEsigi { get; set; } = 60;

    public double EmaAlpha { get; set; } = 0.18;

    public double YuzYokkenDusmeHizi { get; set; } = 6.0;

    public int PipeBaglantiZamanAsimiMs { get; set; } = 15000;

    public int GirdiOrneklemeMs { get; set; } = 100;

    public int AktivitePenceresiSaniye { get; set; } = 60;

    public int HareketsizlikUyariSaniyesi { get; set; } = 45;

    public int BosBakisSaniyesi { get; set; } = 15;

    public double BosBakisCezaPuani { get; set; } = 15;

    public string UyariMesajiNotu { get; set; } = "Kısa bir toparlanma molası verip tekrar odaklan.";

    public int KameraOnizlemeFps { get; set; } = 30;

    public int KameraAnalizFps { get; set; } = 10;

    public double KlavyeDakikaBeklenen { get; set; } = 8;

    public double FarePikselDakikaBeklenen { get; set; } = 900;

    public double DusukAktiviteCezaTavani { get; set; } = 12;

    public double EarCezaKatsayisi { get; set; } = 200;

    public double EarCezaTavani { get; set; } = 25;

    public double GazeEsigi { get; set; } = 0.01;

    public double GazeCezaKatsayisi { get; set; } = 400;

    public double GazeCezaTavani { get; set; } = 30;

    public double PosturEsigi { get; set; } = 8;

    public double PosturCezaKatsayisi { get; set; } = 0.8;

    public double PosturCezaTavani { get; set; } = 20;

    public bool SadecePencereliKaraListeSurecleri { get; set; } = true;

    public bool KaraListeMudahalesiAktif { get; set; }

    public string[] KaraListe { get; set; } =
    [
        "steam",
        "Discord",
        "EpicGamesLauncher",
        "Spotify",
        "EADesktop",
        "upc",
        "XboxApp"
    ];

    public string[] BeyazListe { get; set; } =
    [
        "devenv",
        "Code",
        "notepad",
        "cmd",
        "powershell",
        "WindowsTerminal",
        "python",
        "dotnet",
        "KararMotoru"
    ];

    public void Normalize()
    {
        OdakEsigi = Math.Clamp(OdakEsigi, 1, 99);
        EmaAlpha = Math.Clamp(EmaAlpha, 0.01, 1.0);
        YuzYokkenDusmeHizi = Math.Clamp(YuzYokkenDusmeHizi, 1.0, 25.0);
        PipeBaglantiZamanAsimiMs = Math.Max(1000, PipeBaglantiZamanAsimiMs);
        GirdiOrneklemeMs = Math.Clamp(GirdiOrneklemeMs, 30, 1000);
        AktivitePenceresiSaniye = Math.Clamp(AktivitePenceresiSaniye, 10, 600);
        HareketsizlikUyariSaniyesi = Math.Clamp(HareketsizlikUyariSaniyesi, 5, 3600);
        BosBakisSaniyesi = Math.Clamp(BosBakisSaniyesi, 5, 120);
        BosBakisCezaPuani = Math.Clamp(BosBakisCezaPuani, 0, 50);
        UyariMesajiNotu = string.IsNullOrWhiteSpace(UyariMesajiNotu)
            ? "Kısa bir toparlanma molası verip tekrar odaklan."
            : UyariMesajiNotu.Trim();
        KameraOnizlemeFps = Math.Clamp(KameraOnizlemeFps, 5, 60);
        KameraAnalizFps = Math.Clamp(KameraAnalizFps, 1, 30);
        KlavyeDakikaBeklenen = Math.Max(1, KlavyeDakikaBeklenen);
        FarePikselDakikaBeklenen = Math.Max(1, FarePikselDakikaBeklenen);
        DusukAktiviteCezaTavani = Math.Clamp(DusukAktiviteCezaTavani, 0, 50);
        EarCezaKatsayisi = Math.Clamp(EarCezaKatsayisi, 0, 1000);
        EarCezaTavani = Math.Clamp(EarCezaTavani, 0, 60);
        GazeEsigi = Math.Clamp(GazeEsigi, 0, 0.25);
        GazeCezaKatsayisi = Math.Clamp(GazeCezaKatsayisi, 0, 1000);
        GazeCezaTavani = Math.Clamp(GazeCezaTavani, 0, 60);
        PosturEsigi = Math.Clamp(PosturEsigi, 0, 60);
        PosturCezaKatsayisi = Math.Clamp(PosturCezaKatsayisi, 0, 5);
        PosturCezaTavani = Math.Clamp(PosturCezaTavani, 0, 60);
        KaraListe = ListeyiTemizle(KaraListe);
        BeyazListe = ListeyiTemizle(BeyazListe);
    }

    private static string[] ListeyiTemizle(IEnumerable<string> liste)
    {
        return liste
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
