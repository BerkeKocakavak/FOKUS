using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using FokusKararMotoru.Models;

namespace FokusKararMotoru.Services;

public sealed class DurumYazici
{
    private readonly string _aktifOdakPath;
    private readonly string _durumPath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public DurumYazici(string projeKoku)
    {
        _aktifOdakPath = Path.Combine(projeKoku, "aktif_odak.txt");
        _durumPath = Path.Combine(projeKoku, "karar_motoru_durum.json");
    }

    public void Yaz(
        OdakSonucu odak,
        BiyometrikVeri biyometrikVeri,
        GirdiAktiviteOzeti girdi,
        SurecTaramaSonucu surec)
    {
        AtomikYaz(_aktifOdakPath, odak.Puan.ToString());

        var durum = new
        {
            zaman = DateTimeOffset.Now,
            odak_puani = odak.Puan,
            ham_hedef_puan = Math.Round(odak.HamHedefPuan, 2),
            mudahale_gerekli = odak.MudahaleGerekli,
            cezalar = odak.Cezalar,
            biyometrik = biyometrikVeri,
            girdi,
            surec
        };

        AtomikYaz(_durumPath, JsonSerializer.Serialize(durum, JsonOptions));
    }

    private static void AtomikYaz(string path, string icerik)
    {
        const int maxDeneme = 12;

        for (int deneme = 1; deneme <= maxDeneme; deneme++)
        {
            string tmp = path + "." + Environment.ProcessId + ".tmp";

            try
            {
                File.WriteAllText(tmp, icerik);
                File.Move(tmp, path, overwrite: true);
                return;
            }
            catch (IOException) when (deneme < maxDeneme)
            {
                GuvenliSil(tmp);
                Thread.Sleep(20);
            }
            catch (UnauthorizedAccessException) when (deneme < maxDeneme)
            {
                GuvenliSil(tmp);
                Thread.Sleep(20);
            }
        }
    }

    private static void GuvenliSil(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
