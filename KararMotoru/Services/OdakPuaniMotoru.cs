using FokusKararMotoru.Models;

namespace FokusKararMotoru.Services;

public sealed class OdakPuaniMotoru
{
    private double _oncekiPuan = 100.0;

    public OdakSonucu Hesapla(
        BiyometrikVeri veri,
        GirdiAktiviteOzeti girdi,
        SurecTaramaSonucu surec,
        KararMotoruAyarlari ayarlar)
    {
        var cezalar = new List<CezaKalemi>();

        if (!veri.YuzVar)
        {
            _oncekiPuan = Math.Max(0, _oncekiPuan - ayarlar.YuzYokkenDusmeHizi);
            cezalar.Add(new CezaKalemi
            {
                Kaynak = "Yüz",
                Deger = ayarlar.YuzYokkenDusmeHizi,
                Aciklama = "Kamera kullanıcının yüzünü göremiyor."
            });

            return SonucOlustur(_oncekiPuan, _oncekiPuan, false, cezalar);
        }

        double hedefPuan = 100.0;

        if (veri.EarEsik > 0 && veri.Ear > 0 && veri.Ear < veri.EarEsik)
        {
            double ceza = Math.Min(ayarlar.EarCezaTavani, (veri.EarEsik - veri.Ear) * ayarlar.EarCezaKatsayisi);
            CezaEkle(cezalar, "Göz", ceza, "EAR eşiğin altında; göz kapanması veya uyuklama belirtisi var.");
            hedefPuan -= ceza;
        }

        double gazeSapma = Math.Abs(veri.GazeSapma);
        if (gazeSapma > ayarlar.GazeEsigi)
        {
            double ceza = Math.Min(ayarlar.GazeCezaTavani, gazeSapma * ayarlar.GazeCezaKatsayisi);
            CezaEkle(cezalar, "Bakış", ceza, "Bakış merkezi ekrandan uzaklaşıyor.");
            hedefPuan -= ceza;
        }

        double posturSapma = Math.Sqrt(Math.Pow(veri.OneSapma, 2) + Math.Pow(veri.YanaSapma, 2));
        if (posturSapma > ayarlar.PosturEsigi)
        {
            double ceza = Math.Min(ayarlar.PosturCezaTavani, (posturSapma - ayarlar.PosturEsigi) * ayarlar.PosturCezaKatsayisi);
            CezaEkle(cezalar, "Postür", ceza, "Baş ve gövde referans duruştan uzaklaştı.");
            hedefPuan -= ceza;
        }

        double aktiviteCezasi = AktiviteCezasiHesapla(girdi, ayarlar);
        if (aktiviteCezasi > 0)
        {
            CezaEkle(cezalar, "Girdi", aktiviteCezasi, "Klavye/fare etkinliği beklenen seviyenin altında.");
            hedefPuan -= aktiviteCezasi;
        }

        if (surec.KaraListeCezasi > 0)
        {
            CezaEkle(cezalar, "Kara liste", surec.KaraListeCezasi, "Dikkat dağıtıcı süreç açık görünüyor.");
            hedefPuan -= surec.KaraListeCezasi;
        }

        hedefPuan = Math.Clamp(hedefPuan, 0, 100);
        double puruzsuzPuan = ayarlar.EmaAlpha * hedefPuan + (1.0 - ayarlar.EmaAlpha) * _oncekiPuan;
        _oncekiPuan = puruzsuzPuan;

        bool mudahaleGerekli = puruzsuzPuan < ayarlar.OdakEsigi && surec.KaraListedekiSurecler.Count > 0;
        return SonucOlustur(puruzsuzPuan, hedefPuan, mudahaleGerekli, cezalar);
    }

    private static double AktiviteCezasiHesapla(GirdiAktiviteOzeti girdi, KararMotoruAyarlari ayarlar)
    {
        if (girdi.HareketsizSaniye > ayarlar.HareketsizlikUyariSaniyesi)
        {
            double oran = (girdi.HareketsizSaniye - ayarlar.HareketsizlikUyariSaniyesi) / ayarlar.HareketsizlikUyariSaniyesi;
            return Math.Min(ayarlar.DusukAktiviteCezaTavani, oran * ayarlar.DusukAktiviteCezaTavani);
        }

        double klavyeOrani = Math.Clamp(girdi.TusDakika / ayarlar.KlavyeDakikaBeklenen, 0, 1);
        double fareOrani = Math.Clamp(girdi.FarePikselDakika / ayarlar.FarePikselDakikaBeklenen, 0, 1);
        double aktiviteOrani = Math.Max(klavyeOrani, fareOrani);

        if (aktiviteOrani >= 0.25)
        {
            return 0;
        }

        return Math.Min(ayarlar.DusukAktiviteCezaTavani, (0.25 - aktiviteOrani) * ayarlar.DusukAktiviteCezaTavani);
    }

    private static void CezaEkle(List<CezaKalemi> cezalar, string kaynak, double deger, string aciklama)
    {
        if (deger <= 0)
        {
            return;
        }

        cezalar.Add(new CezaKalemi
        {
            Kaynak = kaynak,
            Deger = Math.Round(deger, 2),
            Aciklama = aciklama
        });
    }

    private static OdakSonucu SonucOlustur(double puan, double hamHedefPuan, bool mudahaleGerekli, IReadOnlyList<CezaKalemi> cezalar)
    {
        return new OdakSonucu
        {
            Puan = (int)Math.Round(Math.Clamp(puan, 0, 100)),
            HamHedefPuan = Math.Clamp(hamHedefPuan, 0, 100),
            MudahaleGerekli = mudahaleGerekli,
            Cezalar = cezalar
        };
    }
}
