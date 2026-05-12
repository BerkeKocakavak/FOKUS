# FOKUS - Fiziksel Odak Koruma ve Uyarı Sistemi

## Proje Ekibi
- Batuhan Sami Akçay - Karar Motoru & Girdi İzleme
- Berke Kocakavak - Bilgisayarlı Görü & Biyometri & Kalibrasyon
- Hasan Gürses - İşletim Sistemi Çekirdeği & Otomasyon
- Türkay Aydoğan - Veri Bilimi, Analitik & Gösterge Paneli

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
Bu komut WPF arayüzünü başlatır; uygulama başlangıçta Python paketlerini hızlıca kontrol eder.

Alternatif olarak:
```bash
dotnet run --project .\KararMotoru\KararMotoru.csproj
```

## Modüller
- `kamera_test.py` - Biyometrik analiz modülü
- `KararMotoru` - WPF arayüz, karar motoru, girdi izleme ve odak analiz modülü
- `bagimlilik_kontrol.py` - Başlangıçta çalışan Python paket kontrolü

## Çalışma Dosyaları
- Ayarlar: `ayarlar/karar_motoru_ayarlar.json`
- Veritabanı: `veriler/fokus.db`
- Anlık durum: `durum/aktif_odak.txt`, `durum/karar_motoru_durum.json`
- Loglar: `loglar/camera_worker.log`, `loglar/karar_motoru.log`
- Analiz modeli: `modeller/face_landmarker.task`

Bu dosyalar uygulama çalışırken otomatik oluşur ve proje ana klasörüne yazılmaz.

## Notlar
- İlk çalıştırmada `modeller/face_landmarker.task` modeli otomatik indirilir.
- Kamera görüntüsü WPF içinde açılır; ayrı OpenCV penceresi normal kullanımda açılmaz.
- Kalibrasyon sırasında 3 saniye düz oturup ekrana bakın.
- Süreç müdahalesi varsayılan olarak kapalıdır; arayüzden açılır.
- Oturum geçmişi ve odak örnekleri `veriler/fokus.db` SQLite veritabanına yazılır.
