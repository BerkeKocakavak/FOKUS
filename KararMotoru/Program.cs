using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading.Tasks;

namespace FokusKararMotoru
{
    public class BiyometrikVeri
    {
        public double zaman { get; set; }
        public double ear { get; set; }
        public double ear_esik { get; set; }
        public double gaze { get; set; }
        public double gaze_sapma { get; set; }
        public double one_sapma { get; set; }
        public double yana_sapma { get; set; }
        public int kirpma_sayisi { get; set; }
        public bool yuz_var { get; set; }
    }

    class Program
    {
        // YENİ KARA LİSTE:
        static readonly string[] KaraListe = {
            "steam", "Discord", "EpicGamesLauncher", "Spotify",
            "EADesktop", // EA App
            "upc",       // Ubisoft Connect
            "XboxApp"    // XBOX Uygulaması
        };

        static string aktifYasakliUygulama = "Yok";

        static async Task Main(string[] args)
        {
            Console.WriteLine("FOKUS Karar Motoru Başlatılıyor...");
            Console.WriteLine("Python Pipe'ına bağlanılması bekleniyor...");

            using (var client = new NamedPipeClientStream(".", "fokus_pipe", PipeDirection.In))
            {
                try
                {
                    await client.ConnectAsync();

                    using (var reader = new StreamReader(client))
                    {
                        while (client.IsConnected)
                        {
                            string jsonVeri = await reader.ReadLineAsync();
                            if (!string.IsNullOrEmpty(jsonVeri))
                            {
                                try
                                {
                                    BiyometrikVeri veri = JsonSerializer.Deserialize<BiyometrikVeri>(jsonVeri);

                                    int arkaPlanCezasi = KaraListeKontrol();
                                    int odakPuani = OdakPuaniHesapla(veri, arkaPlanCezasi);

                                    // YENİ EKLENEN KISIM: Gerçek Puanı Python'ın okuması için txt'ye yazıyoruz
                                    // (..\ diyerek bir üst klasöre, yani FOKUS-main içine kaydediyoruz)
                                    File.WriteAllText(@"..\aktif_odak.txt", odakPuani.ToString());

                                    Console.SetCursorPosition(0, 0);
                                    Console.WriteLine("================ FOKUS KARAR MOTORU ================");
                                    Console.WriteLine($"Durum:           {(veri.yuz_var ? "Kullanıcı Ekran Başında" : "KULLANICI YOK!").PadRight(30)}");
                                    Console.WriteLine($"Göz Kırpma:      {veri.kirpma_sayisi.ToString().PadRight(30)}");
                                    Console.WriteLine($"Baş Eğimi:       (Öne: {veri.one_sapma}, Yana: {veri.yana_sapma})".PadRight(40));

                                    Console.Write("Arka Plan Tespit: ");
                                    if (aktifYasakliUygulama != "Yok") Console.ForegroundColor = ConsoleColor.Red;
                                    Console.WriteLine(aktifYasakliUygulama.PadRight(50));
                                    Console.ResetColor();

                                    Console.WriteLine("----------------------------------------------------");
                                    Console.Write("ANLIK ODAK PUANI: ");
                                    if (odakPuani > 70) Console.ForegroundColor = ConsoleColor.Green;
                                    else if (odakPuani > 40) Console.ForegroundColor = ConsoleColor.Yellow;
                                    else Console.ForegroundColor = ConsoleColor.Red;

                                    Console.WriteLine($"{odakPuani} / 100".PadRight(20));
                                    Console.ResetColor();
                                    Console.WriteLine("====================================================");
                                }
                                catch (JsonException) { }
                            }
                        }
                    }
                }
                catch (Exception ex) { Console.WriteLine($"Hata oluştu: {ex.Message}"); }
            }
        }

        // GÜNCELLENMİŞ KONTROL: 
        static int KaraListeKontrol()
        {
            int ceza = 0;
            System.Collections.Generic.List<string> acikUygulamalar = new System.Collections.Generic.List<string>();

            foreach (var uygulama in KaraListe)
            {
                var processes = Process.GetProcessesByName(uygulama);

                foreach (var proc in processes)
                {
                    // SİHİRLİ DOKUNUŞ 2.0: Sadece Handle (Pencere Kimliği) yeterli. 
                    // Title kontrolünü kaldırdık ki Steam gizlenemesin!
                    if (proc.MainWindowHandle != IntPtr.Zero)
                    {
                        if (!acikUygulamalar.Contains(uygulama))
                        {
                            acikUygulamalar.Add(uygulama);
                        }
                        break;
                    }
                }
            }

            int count = acikUygulamalar.Count;
            if (count > 0)
            {
                if (count == 1) ceza = 30;
                else if (count == 2) ceza = 50;
                else if (count == 3) ceza = 65;
                else if (count == 4) ceza = 75;
                else if (count == 5) ceza = 90;
                else if (count >= 6) ceza = 100;

                aktifYasakliUygulama = $"{count} Uygulama ({string.Join(", ", acikUygulamalar)}) [CEZA: -{ceza}]";
            }
            else
            {
                aktifYasakliUygulama = "Yok";
            }

            return ceza;
        }

        // EMA Algoritması için geçmiş puanı hafızada tutmamız lazım
        static double oncekiPuan = 100.0;

        // FOKUS V2.0 Karmaşık ve Pürüzsüz Odak Algoritması
        static int OdakPuaniHesapla(BiyometrikVeri veri, int arkaPlanCezasi)
        {
            if (!veri.yuz_var)
            {
                // Yüz kameradan çıkarsa anında 0'a çakılmasın, hızla azalsın (Eriyerek bitsin)
                oncekiPuan = Math.Max(0, oncekiPuan - 5.0);
                return (int)oncekiPuan;
            }

            double anlikHedef = 100.0;

            // 1. GÖZ KIRPMA / UYUKLAMA MATEMATİĞİ (Max -25 Puan)
            if (veri.ear < veri.ear_esik)
            {
                // Eşiğin ne kadar altına düştüğüne bağlı oransal ceza (Göz ne kadar kapalıysa o kadar ceza)
                double fark = veri.ear_esik - veri.ear;
                anlikHedef -= Math.Min(25, fark * 200);
            }

            // 2. BAKIŞ (GAZE) SİYMETRİSİ MATEMATİĞİ (Max -30 Puan)
            double gazeSapma = Math.Abs(veri.gaze_sapma);
            if (gazeSapma > 0.01)
            {
                // Ekrana bakmıyorsa, merkezden uzaklaştıkça artan ceza
                anlikHedef -= Math.Min(30, gazeSapma * 400);
            }

            // 3. POSTÜR / DURUŞ BOZUKLUĞU MATEMATİĞİ (Max -20 Puan)
            // Öklid uzaklığı ile toplam sapma vektörünü buluyoruz
            double posturSapma = Math.Sqrt(Math.Pow(veri.one_sapma, 2) + Math.Pow(veri.yana_sapma, 2));
            if (posturSapma > 8)
            {
                anlikHedef -= Math.Min(20, (posturSapma - 8) * 0.8);
            }

            // 4. KARA LİSTE OTORİTESİ (En ağır darbe)
            anlikHedef -= arkaPlanCezasi;

            // Hedef puanı 0-100 arasına kilitle
            anlikHedef = Math.Max(0, Math.Min(100, anlikHedef));

            // 5. ÜSTEL HAREKETLİ ORTALAMA (EMA) - Pürüzsüzleştirme Motoru
            // alpha = 0.15 demek: Puanın %15'i anlık durumdan, %85'i geçmiş durumdan gelir. Titremeyi yok eder!
            double alpha = 0.15;
            double pürüzsüzPuan = (alpha * anlikHedef) + ((1.0 - alpha) * oncekiPuan);

            // Gelecek döngü için hafızayı güncelle
            oncekiPuan = pürüzsüzPuan;

            return (int)pürüzsüzPuan;
        }
    }
}