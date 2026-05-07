# FOKUS - Fiziksel Odak Koruma ve Uyarı Sistemi

## Proje Ekibi
- Batuhan Sami Akçay – Karar Motoru & Girdi İzleme
- Berke Kocakavak – Bilgisayarlı Görü & Biyometri & Kalibrasyon
- Hasan Gürses – İşletim Sistemi Çekirdeği & Otomasyon
- Türkay Aydoğan – Veri Bilimi, Analitik & Gösterge Paneli

## Gereksinimler
- Python 3.12.9
- .NET 10.0
- .NET SDK

## Kurulum

### Python tarafı
```bash
python -m venv venv
venv\Scripts\activate
pip install -r requirements.txt
```

### Çalıştırma
```bash
ÇALIŞTIR.bat
```
Bu komut bağımlılıkları kontrol eder ve WPF arayüzünü başlatır.

Alternatif olarak:
```bash
dotnet run --project .\KararMotoru\KararMotoru.csproj
```

## Modüller
- `kamera_test.py` — Biyometrik analiz modülü (Berke)
- `KararMotoru` — WPF arayüz, karar motoru, girdi izleme ve odak analiz modülü (Batuhan Sami)
- `bagimlilik_kontrol.py` — Başlangıçta çalışan Python paket kontrolü

## Notlar
- İlk çalıştırmada `face_landmarker.task` modeli otomatik indirilir (~30MB)
- Kamera görüntüsü WPF içinde açılır; ayrı OpenCV penceresi normal kullanımda açılmaz.
- Kalibrasyon sırasında 3 saniye düz oturup ekrana bakın.
- Süreç müdahalesi varsayılan olarak kapalıdır; arayüzden açılır.
- Oturum geçmişi `odak_oturum_gecmisi.csv` dosyasına yazılır.
