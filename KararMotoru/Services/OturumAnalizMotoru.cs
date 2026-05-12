namespace FokusKararMotoru.Services;

public sealed class OturumAnalizMotoru
{
    public OturumAnalizSonucu Uret(SessionEndAnalysis analysis, int focusThreshold)
    {
        var bulgular = new List<string>();
        var oneriler = new List<string>();

        string derece = analysis.AverageFocus >= 80
            ? "Güçlü"
            : analysis.AverageFocus >= focusThreshold
                ? "Dengeli"
                : "Dikkat istiyor";

        if (analysis.SampleCount == 0)
        {
            return new OturumAnalizSonucu(
                "Veri yok",
                "Bu oturumda analiz edilecek odak örneği kaydedilmedi.",
                ["Kamera ve pipe bağlantısını kontrol edip yeni bir oturum başlat."],
                ["Kısa bir test oturumu çalıştırarak kayıt akışını doğrula."]);
        }

        bulgular.Add($"Ortalama odak {analysis.AverageFocus:0.0}; minimum odak {analysis.MinimumFocus}.");

        if (analysis.LowFocusRate >= 0.35)
        {
            bulgular.Add($"Düşük odak oranı yüksek: {analysis.LowFocusRate:P0}.");
            oneriler.Add("Bir sonraki oturumda 20-25 dakikalık daha kısa çalışma bloğu dene.");
        }
        else if (analysis.LowFocusRate > 0)
        {
            bulgular.Add($"Düşük odak kısa aralıklarla görüldü: {analysis.LowFocusRate:P0}.");
        }
        else
        {
            bulgular.Add("Oturum boyunca odak eşiğinin altına inilmedi.");
        }

        if (analysis.BlacklistSamples > 0)
        {
            string karaListe = analysis.Blacklist.Count == 0
                ? "kara listedeki uygulamalar"
                : string.Join(", ", analysis.Blacklist.Take(3).Select(item => item.ProcessName));
            bulgular.Add($"Kara liste {analysis.BlacklistSamples} örnekte yakalandı: {karaListe}.");
            oneriler.Add("Çalışmaya başlamadan önce dikkat dağıtan uygulamaları kapat veya beyaz liste modunda kal.");
        }

        if (analysis.FaceMissingSamples > Math.Max(3, analysis.SampleCount * 0.15))
        {
            bulgular.Add("Kamera bazı anlarda yüzü göremedi.");
            oneriler.Add("Kamera açısını ve ışığı sabitle; yüz kaybı odak puanını gereksiz düşürebilir.");
        }

        if (analysis.AverageIdleSeconds >= 25)
        {
            bulgular.Add($"Ortalama hareketsizlik {analysis.AverageIdleSeconds:0} saniye.");
            oneriler.Add("Uzun okuma yapmıyorsan görevi küçük yazma/işaretleme adımlarına böl.");
        }

        if (analysis.Penalties.Count > 0)
        {
            PenaltySummary enBuyukCeza = analysis.Penalties[0];
            bulgular.Add($"En baskın ceza kaynağı: {enBuyukCeza.Source}.");
            oneriler.Add(CezaOnerisi(enBuyukCeza.Source));
        }

        if (analysis.InterventionSamples > 0)
        {
            bulgular.Add($"{analysis.InterventionSamples} örnekte müdahale koşulu oluştu.");
        }

        if (oneriler.Count == 0)
        {
            oneriler.Add("Bu ritmi koru; benzer süre ve ortamla devam etmek mantıklı.");
        }

        string ozet = derece switch
        {
            "Güçlü" => "Oturum genel olarak verimli geçti.",
            "Dengeli" => "Oturum kullanılabilir seviyede; birkaç küçük iyileştirme alanı var.",
            _ => "Oturumda dikkat dağıtan veya biyometrik olarak zorlayan noktalar var."
        };

        return new OturumAnalizSonucu(derece, ozet, bulgular.Take(6).ToArray(), oneriler.Distinct().Take(5).ToArray());
    }

    private static string CezaOnerisi(string kaynak)
    {
        return kaynak.ToLowerInvariant() switch
        {
            string value when value.Contains("göz") => "Göz yorgunluğu için kısa ekran molaları ve daha iyi aydınlatma kullan.",
            string value when value.Contains("bakış") => "Çalıştığın pencereyi merkeze al ve ikinci ekran/telefon dikkatini azalt.",
            string value when value.Contains("postür") => "Oturma pozisyonunu ve ekran yüksekliğini oturum başında düzelt.",
            string value when value.Contains("aktivite") => "Pasif kalıyorsan not alma veya küçük görev adımlarıyla etkileşimi artır.",
            string value when value.Contains("kara") => "Kara listedeki uygulamaları oturum başlamadan kapat.",
            _ => "En sık ceza kaynağını azaltacak küçük bir ortam düzenlemesi yap."
        };
    }
}

public sealed record OturumAnalizSonucu(
    string Derece,
    string Ozet,
    IReadOnlyList<string> Bulgular,
    IReadOnlyList<string> Oneriler);
