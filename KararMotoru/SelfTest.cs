using FokusKararMotoru.Models;
using FokusKararMotoru.Services;

namespace FokusKararMotoru;

public static class SelfTest
{
    public static int Run()
    {
        var ayarlar = new KararMotoruAyarlari
        {
            EmaAlpha = 1.0
        };
        ayarlar.Normalize();

        var motor = new OdakPuaniMotoru();
        var aktifGirdi = new GirdiAktiviteOzeti
        {
            TusDakika = 12,
            FarePikselDakika = 1200,
            HareketsizSaniye = 2
        };

        var normalSurec = new SurecTaramaSonucu();
        var normalVeri = new BiyometrikVeri
        {
            YuzVar = true,
            Ear = 0.28,
            EarEsik = 0.20,
            GazeSapma = 0,
            OneSapma = 0,
            YanaSapma = 0
        };

        OdakSonucu normal = motor.Hesapla(normalVeri, aktifGirdi, normalSurec, ayarlar);
        Dogrula(normal.Puan >= 95, "Normal senaryoda odak puanı yüksek olmalı.");

        var daginikSurec = new SurecTaramaSonucu
        {
            KaraListedekiSurecler = ["Discord"],
            KaraListeCezasi = 30
        };
        var daginikVeri = new BiyometrikVeri
        {
            YuzVar = true,
            Ear = 0.12,
            EarEsik = 0.20,
            GazeSapma = 0.05,
            OneSapma = 18,
            YanaSapma = 4
        };

        OdakSonucu daginik = motor.Hesapla(daginikVeri, aktifGirdi, daginikSurec, ayarlar);
        Dogrula(daginik.Puan < normal.Puan, "Dağınık senaryoda odak puanı düşmeli.");
        Dogrula(daginik.MudahaleGerekli, "Kara liste ve düşük puan varsa müdahale önerilmeli.");

        var yuzYok = new BiyometrikVeri
        {
            YuzVar = false
        };

        OdakSonucu yuzYokSonuc = motor.Hesapla(yuzYok, aktifGirdi, normalSurec, ayarlar);
        Dogrula(yuzYokSonuc.Puan < normal.Puan, "Yüz yokken puan azalmalı.");

        Console.WriteLine("Karar motoru özdenetimi geçti.");
        return 0;
    }

    private static void Dogrula(bool kosul, string mesaj)
    {
        if (!kosul)
        {
            throw new InvalidOperationException("Özdenetim başarısız: " + mesaj);
        }
    }
}
