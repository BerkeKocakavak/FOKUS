using System.IO;

namespace FokusKararMotoru.Services;

public static class UygulamaKlasorleri
{
    private const string AyarlarKlasoru = "ayarlar";
    private const string VerilerKlasoru = "veriler";
    private const string DurumKlasoru = "durum";
    private const string LoglarKlasoru = "loglar";
    private const string ModellerKlasoru = "modeller";

    public static string Ayarlar(string projeKoku) => Klasor(projeKoku, AyarlarKlasoru);

    public static string Veriler(string projeKoku) => Klasor(projeKoku, VerilerKlasoru);

    public static string Durum(string projeKoku) => Klasor(projeKoku, DurumKlasoru);

    public static string Loglar(string projeKoku) => Klasor(projeKoku, LoglarKlasoru);

    public static string Modeller(string projeKoku) => Klasor(projeKoku, ModellerKlasoru);

    public static string AyarDosyasi(string projeKoku) =>
        Path.Combine(Ayarlar(projeKoku), "karar_motoru_ayarlar.json");

    public static string VeritabaniDosyasi(string projeKoku) =>
        Path.Combine(Veriler(projeKoku), "fokus.db");

    public static string AktifOdakDosyasi(string projeKoku) =>
        Path.Combine(Durum(projeKoku), "aktif_odak.txt");

    public static string KararMotoruDurumDosyasi(string projeKoku) =>
        Path.Combine(Durum(projeKoku), "karar_motoru_durum.json");

    public static string KameraLogDosyasi(string projeKoku) =>
        Path.Combine(Loglar(projeKoku), "camera_worker.log");

    private static string Klasor(string projeKoku, string klasorAdi)
    {
        string path = Path.Combine(projeKoku, klasorAdi);
        Directory.CreateDirectory(path);
        return path;
    }
}
