using System.Text.Json.Serialization;

namespace FokusKararMotoru.Models;

public sealed class BiyometrikVeri
{
    [JsonPropertyName("zaman")]
    public double Zaman { get; set; }

    [JsonPropertyName("ear")]
    public double Ear { get; set; }

    [JsonPropertyName("ear_esik")]
    public double EarEsik { get; set; }

    [JsonPropertyName("gaze")]
    public double Gaze { get; set; }

    [JsonPropertyName("gaze_sapma")]
    public double GazeSapma { get; set; }

    [JsonPropertyName("one_sapma")]
    public double OneSapma { get; set; }

    [JsonPropertyName("yana_sapma")]
    public double YanaSapma { get; set; }

    [JsonPropertyName("kirpma_sayisi")]
    public int KirpmaSayisi { get; set; }

    [JsonPropertyName("yuz_var")]
    public bool YuzVar { get; set; }

    [JsonPropertyName("gaze_yon")]
    public string? GazeYon { get; set; }

    [JsonPropertyName("bas_durum")]
    public string? BasDurum { get; set; }

    [JsonPropertyName("kalibrasyon_tamam")]
    public bool KalibrasyonTamam { get; set; }

    [JsonPropertyName("kalibrasyon_kalan_saniye")]
    public int KalibrasyonKalanSaniye { get; set; }

    [JsonPropertyName("analiz_hazir")]
    public bool AnalizHazir { get; set; } = true;

    [JsonPropertyName("analiz_durumu")]
    public string? AnalizDurumu { get; set; }

}
