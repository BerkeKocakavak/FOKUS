using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using FokusKararMotoru.Models;

namespace FokusKararMotoru.Services;

public static class AyarDeposu
{
    private const string AyarDosyasiAdi = "karar_motoru_ayarlar.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true
    };

    public static string AyarDosyasiYolu(string projeKoku) => Path.Combine(projeKoku, AyarDosyasiAdi);

    public static KararMotoruAyarlari YukleVeyaOlustur(string projeKoku)
    {
        string path = AyarDosyasiYolu(projeKoku);

        KararMotoruAyarlari ayarlar;
        if (!File.Exists(path))
        {
            ayarlar = new KararMotoruAyarlari();
            ayarlar.Normalize();
            File.WriteAllText(path, JsonSerializer.Serialize(ayarlar, JsonOptions));
            return ayarlar;
        }

        try
        {
            string json = File.ReadAllText(path);
            ayarlar = JsonSerializer.Deserialize<KararMotoruAyarlari>(json, JsonOptions) ?? new KararMotoruAyarlari();
        }
        catch (JsonException)
        {
            string bozukDosya = path + ".bozuk";
            File.Move(path, bozukDosya, overwrite: true);
            ayarlar = new KararMotoruAyarlari();
            File.WriteAllText(path, JsonSerializer.Serialize(ayarlar, JsonOptions));
        }

        ayarlar.Normalize();
        return ayarlar;
    }

    public static void Kaydet(string projeKoku, KararMotoruAyarlari ayarlar)
    {
        ayarlar.Normalize();
        File.WriteAllText(AyarDosyasiYolu(projeKoku), JsonSerializer.Serialize(ayarlar, JsonOptions));
    }
}
