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

    public double BeyazListeAktifkenKaraListeCezaKatsayisi { get; set; } = 0.45;

    public int[] KaraListeCezaBasamaklari { get; set; } = [0, 30, 50, 65, 75, 90, 100];

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
        KlavyeDakikaBeklenen = Math.Max(1, KlavyeDakikaBeklenen);
        FarePikselDakikaBeklenen = Math.Max(1, FarePikselDakikaBeklenen);
        BeyazListeAktifkenKaraListeCezaKatsayisi = Math.Clamp(BeyazListeAktifkenKaraListeCezaKatsayisi, 0, 1);

        if (KaraListeCezaBasamaklari.Length == 0)
        {
            KaraListeCezaBasamaklari = [0, 30, 50, 65, 75, 90, 100];
        }
    }
}
