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
}
