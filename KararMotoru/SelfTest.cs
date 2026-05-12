using FokusKararMotoru.Models;
using FokusKararMotoru.Services;
using System.IO;

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
            AnalizHazir = true,
            KalibrasyonTamam = true,
            Ear = 0.28,
            EarEsik = 0.20,
            GazeSapma = 0,
            OneSapma = 0,
            YanaSapma = 0
        };

        OdakSonucu normal = motor.Hesapla(normalVeri, aktifGirdi, normalSurec, ayarlar);
        Dogrula(normal.Puan >= 95, "Normal senaryoda odak puanı yüksek olmalı.");

        var karaListeSureci = new SurecTaramaSonucu
        {
            KaraListedekiSurecler = ["Discord"],
            KaraListeCezasi = 30
        };
        OdakSonucu sadeceKaraListe = new OdakPuaniMotoru().Hesapla(normalVeri, aktifGirdi, karaListeSureci, ayarlar);
        Dogrula(sadeceKaraListe.Puan >= 95, "Kara liste tek başına odak puanını düşürmemeli.");
        Dogrula(sadeceKaraListe.Cezalar.All(c => c.Kaynak != "Kara liste"), "Kara liste cezası üretilmemeli.");

        var daginikVeri = new BiyometrikVeri
        {
            YuzVar = true,
            AnalizHazir = true,
            KalibrasyonTamam = true,
            Ear = 0.12,
            EarEsik = 0.20,
            GazeSapma = 0.05,
            OneSapma = 18,
            YanaSapma = 4
        };

        OdakSonucu daginik = motor.Hesapla(daginikVeri, aktifGirdi, karaListeSureci, ayarlar);
        Dogrula(daginik.Puan < normal.Puan, "Dağınık senaryoda odak puanı düşmeli.");
        Dogrula(daginik.MudahaleGerekli, "Kara liste ve düşük puan varsa müdahale önerilmeli.");
        Dogrula(daginik.Cezalar.Any(c => c.Kaynak == "Göz"), "Göz cezası üretilmeli.");
        Dogrula(daginik.Cezalar.Any(c => c.Kaynak == "Bakış"), "Bakış cezası üretilmeli.");
        Dogrula(daginik.Cezalar.Any(c => c.Kaynak == "Postür"), "Postür cezası üretilmeli.");

        var esnekBakisAyarlari = new KararMotoruAyarlari
        {
            EmaAlpha = 1.0,
            GazeEsigi = 0.20,
            GazeCezaKatsayisi = ayarlar.GazeCezaKatsayisi
        };
        esnekBakisAyarlari.Normalize();
        var bakisSapmasi = new BiyometrikVeri
        {
            YuzVar = true,
            AnalizHazir = true,
            KalibrasyonTamam = true,
            Ear = 0.28,
            EarEsik = 0.20,
            GazeSapma = 0.05
        };
        OdakSonucu sikiBakis = new OdakPuaniMotoru().Hesapla(bakisSapmasi, aktifGirdi, normalSurec, ayarlar);
        OdakSonucu esnekBakis = new OdakPuaniMotoru().Hesapla(bakisSapmasi, aktifGirdi, normalSurec, esnekBakisAyarlari);
        Dogrula(sikiBakis.Puan < esnekBakis.Puan, "Bakış eşiği ayarı odak puanını değiştirmeli.");

        var yuzYok = new BiyometrikVeri
        {
            YuzVar = false,
            AnalizHazir = true,
            KalibrasyonTamam = true
        };

        OdakSonucu yuzYokSonuc = motor.Hesapla(yuzYok, aktifGirdi, normalSurec, ayarlar);
        Dogrula(yuzYokSonuc.Puan < normal.Puan, "Yüz yokken puan azalmalı.");

        var kalibrasyon = new BiyometrikVeri
        {
            YuzVar = true,
            AnalizHazir = true,
            KalibrasyonTamam = false,
            Ear = 0.05,
            EarEsik = 0.20,
            GazeSapma = 0.10,
            OneSapma = 30
        };
        OdakSonucu kalibrasyonSonuc = new OdakPuaniMotoru().Hesapla(kalibrasyon, aktifGirdi, normalSurec, ayarlar);
        Dogrula(kalibrasyonSonuc.Cezalar.Count == 0, "Kalibrasyon sırasında ceza üretilmemeli.");

        VeritabaniSelfTest(normal, normalVeri, aktifGirdi, normalSurec);

        Console.WriteLine("Karar motoru özdenetimi geçti.");
        return 0;
    }

    private static void VeritabaniSelfTest(
        OdakSonucu odak,
        BiyometrikVeri biyometrik,
        GirdiAktiviteOzeti girdi,
        SurecTaramaSonucu surec)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "fokus-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var db = new FokusDb(tempDir);
            db.EnsureCreated();
            string sessionId = "self-test";
            db.StartSession(sessionId, DateTimeOffset.Now);
            db.SaveSample(sessionId, new KararMotoruState
            {
                Zaman = DateTimeOffset.Now,
                PipeBagli = true,
                Biyometrik = biyometrik,
                Girdi = girdi,
                Surec = surec,
                Odak = odak
            });
            db.EndSession(sessionId, DateTimeOffset.Now);

            IReadOnlyList<SessionSummary> summaries = db.GetSessionSummaries(5, 60);
            Dogrula(summaries.Count > 0, "Veritabanı oturum raporu üretmeli.");
            Dogrula(summaries[0].SampleCount > 0, "Veritabanı odak örneği kaydetmeli.");

            DateTimeOffset ikinciBaslangic = DateTimeOffset.Now.AddMinutes(-5);
            db.StartSession("self-test-2", ikinciBaslangic);
            db.EndSession("self-test-2", ikinciBaslangic.AddMinutes(2));
            DateTimeOffset ucuncuBaslangic = DateTimeOffset.Now.AddMinutes(-2);
            db.StartSession("self-test-3", ucuncuBaslangic);
            db.EndSession("self-test-3", ucuncuBaslangic.AddMinutes(1));

            DashboardSnapshot snapshot = db.GetDashboardSnapshot(1, 60);
            Dogrula(snapshot.Overview.SessionCount >= 3, "İstatistik özeti son oturum limitiyle sınırlanmamalı.");
            Dogrula(snapshot.Overview.TotalDuration.TotalSeconds > 0, "İstatistik özeti oturum süresini toplamalı.");
        }
        finally
        {
            foreach (string file in Directory.GetFiles(tempDir))
            {
                File.Delete(file);
            }

            Directory.Delete(tempDir);
        }
    }

    private static void Dogrula(bool kosul, string mesaj)
    {
        if (!kosul)
        {
            throw new InvalidOperationException("Özdenetim başarısız: " + mesaj);
        }
    }
}
