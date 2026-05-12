using System.Globalization;
using System.Windows;
using FokusKararMotoru.Models;

namespace FokusKararMotoru;

public partial class SettingsWindow : Window
{
    private KararMotoruAyarlari _ayarlar;

    public SettingsWindow(KararMotoruAyarlari ayarlar)
    {
        InitializeComponent();
        _ayarlar = Kopyala(ayarlar);
        ApplySettings(ayarlar);
    }

    public event EventHandler<SettingsSavedEventArgs>? SettingsSaved;

    public void ApplySettings(KararMotoruAyarlari ayarlar)
    {
        _ayarlar = Kopyala(ayarlar);
        ThresholdTextBox.Text = _ayarlar.OdakEsigi.ToString(CultureInfo.CurrentCulture);
        PreviewFpsTextBox.Text = _ayarlar.KameraOnizlemeFps.ToString(CultureInfo.CurrentCulture);
        AnalysisFpsTextBox.Text = _ayarlar.KameraAnalizFps.ToString(CultureInfo.CurrentCulture);
        KeyboardExpectedTextBox.Text = _ayarlar.KlavyeDakikaBeklenen.ToString("0.##", CultureInfo.CurrentCulture);
        MouseExpectedTextBox.Text = _ayarlar.FarePikselDakikaBeklenen.ToString("0.##", CultureInfo.CurrentCulture);
        EarPenaltyFactorTextBox.Text = _ayarlar.EarCezaKatsayisi.ToString("0.##", CultureInfo.CurrentCulture);
        EarPenaltyCapTextBox.Text = _ayarlar.EarCezaTavani.ToString("0.##", CultureInfo.CurrentCulture);
        GazeThresholdTextBox.Text = _ayarlar.GazeEsigi.ToString("0.###", CultureInfo.CurrentCulture);
        GazePenaltyFactorTextBox.Text = _ayarlar.GazeCezaKatsayisi.ToString("0.##", CultureInfo.CurrentCulture);
        PostureThresholdTextBox.Text = _ayarlar.PosturEsigi.ToString("0.##", CultureInfo.CurrentCulture);
        PosturePenaltyFactorTextBox.Text = _ayarlar.PosturCezaKatsayisi.ToString("0.##", CultureInfo.CurrentCulture);
        BlacklistSettingsTextBox.Text = string.Join(Environment.NewLine, _ayarlar.KaraListe);
        WhitelistSettingsTextBox.Text = string.Join(Environment.NewLine, _ayarlar.BeyazListe);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(ThresholdTextBox.Text, out int odakEsigi))
        {
            StatusText.Text = "Odak eşiği sayı olmalı.";
            return;
        }

        if (!int.TryParse(PreviewFpsTextBox.Text, out int previewFps) ||
            !int.TryParse(AnalysisFpsTextBox.Text, out int analysisFps))
        {
            StatusText.Text = "FPS değerleri sayı olmalı.";
            return;
        }

        if (!DoubleOku(KeyboardExpectedTextBox.Text, out double klavyeBeklenen) ||
            !DoubleOku(MouseExpectedTextBox.Text, out double fareBeklenen) ||
            !DoubleOku(EarPenaltyFactorTextBox.Text, out double earCezaKatsayisi) ||
            !DoubleOku(EarPenaltyCapTextBox.Text, out double earCezaTavani) ||
            !DoubleOku(GazeThresholdTextBox.Text, out double gazeEsigi) ||
            !DoubleOku(GazePenaltyFactorTextBox.Text, out double gazeCezaKatsayisi) ||
            !DoubleOku(PostureThresholdTextBox.Text, out double posturEsigi) ||
            !DoubleOku(PosturePenaltyFactorTextBox.Text, out double posturCezaKatsayisi))
        {
            StatusText.Text = "Hassasiyet alanları sayı olmalı.";
            return;
        }

        KararMotoruAyarlari yeni = Kopyala(_ayarlar);
        yeni.OdakEsigi = odakEsigi;
        yeni.KameraOnizlemeFps = previewFps;
        yeni.KameraAnalizFps = analysisFps;
        yeni.KlavyeDakikaBeklenen = klavyeBeklenen;
        yeni.FarePikselDakikaBeklenen = fareBeklenen;
        yeni.EarCezaKatsayisi = earCezaKatsayisi;
        yeni.EarCezaTavani = earCezaTavani;
        yeni.GazeEsigi = gazeEsigi;
        yeni.GazeCezaKatsayisi = gazeCezaKatsayisi;
        yeni.PosturEsigi = posturEsigi;
        yeni.PosturCezaKatsayisi = posturCezaKatsayisi;
        yeni.KaraListe = ListeOku(BlacklistSettingsTextBox.Text);
        yeni.BeyazListe = ListeOku(WhitelistSettingsTextBox.Text);
        yeni.Normalize();

        bool previewFpsChanged = yeni.KameraOnizlemeFps != _ayarlar.KameraOnizlemeFps;
        bool analysisFpsChanged = yeni.KameraAnalizFps != _ayarlar.KameraAnalizFps;
        _ayarlar = Kopyala(yeni);
        StatusText.Text = "Kaydedildi.";
        SettingsSaved?.Invoke(this, new SettingsSavedEventArgs(yeni, previewFpsChanged, analysisFpsChanged));
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private static bool DoubleOku(string text, out double value)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
               double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string[] ListeOku(string text)
    {
        return text
            .Split(["\r\n", "\n", "\r", ","], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static KararMotoruAyarlari Kopyala(KararMotoruAyarlari kaynak)
    {
        return new KararMotoruAyarlari
        {
            OdakEsigi = kaynak.OdakEsigi,
            EmaAlpha = kaynak.EmaAlpha,
            YuzYokkenDusmeHizi = kaynak.YuzYokkenDusmeHizi,
            PipeBaglantiZamanAsimiMs = kaynak.PipeBaglantiZamanAsimiMs,
            GirdiOrneklemeMs = kaynak.GirdiOrneklemeMs,
            AktivitePenceresiSaniye = kaynak.AktivitePenceresiSaniye,
            HareketsizlikUyariSaniyesi = kaynak.HareketsizlikUyariSaniyesi,
            KameraOnizlemeFps = kaynak.KameraOnizlemeFps,
            KameraAnalizFps = kaynak.KameraAnalizFps,
            KlavyeDakikaBeklenen = kaynak.KlavyeDakikaBeklenen,
            FarePikselDakikaBeklenen = kaynak.FarePikselDakikaBeklenen,
            DusukAktiviteCezaTavani = kaynak.DusukAktiviteCezaTavani,
            EarCezaKatsayisi = kaynak.EarCezaKatsayisi,
            EarCezaTavani = kaynak.EarCezaTavani,
            GazeEsigi = kaynak.GazeEsigi,
            GazeCezaKatsayisi = kaynak.GazeCezaKatsayisi,
            GazeCezaTavani = kaynak.GazeCezaTavani,
            PosturEsigi = kaynak.PosturEsigi,
            PosturCezaKatsayisi = kaynak.PosturCezaKatsayisi,
            PosturCezaTavani = kaynak.PosturCezaTavani,
            SadecePencereliKaraListeSurecleri = kaynak.SadecePencereliKaraListeSurecleri,
            BeyazListeAktifkenKaraListeCezaKatsayisi = kaynak.BeyazListeAktifkenKaraListeCezaKatsayisi,
            KaraListeCezaBasamaklari = kaynak.KaraListeCezaBasamaklari.ToArray(),
            KaraListe = kaynak.KaraListe.ToArray(),
            BeyazListe = kaynak.BeyazListe.ToArray()
        };
    }
}

public sealed class SettingsSavedEventArgs : EventArgs
{
    public SettingsSavedEventArgs(KararMotoruAyarlari ayarlar, bool previewFpsChanged, bool analysisFpsChanged)
    {
        Ayarlar = ayarlar;
        PreviewFpsChanged = previewFpsChanged;
        AnalysisFpsChanged = analysisFpsChanged;
    }

    public KararMotoruAyarlari Ayarlar { get; }

    public bool PreviewFpsChanged { get; }

    public bool AnalysisFpsChanged { get; }
}
