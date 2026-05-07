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
python kamera_test.py
```
Bu komut kamera modülünü açar ve C# karar motorunu arka planda otomatik başlatır.

## Pipe (C#) tarafı - Manuel
Normal kullanımda gerekmez. Karar motorunu tek başına test etmek isterseniz:
```bash
cd KararMotoru
dotnet run
```

## Modüller
- `kamera_test.py` — Biyometrik analiz modülü (Berke)
- `KararMotoru` — Karar motoru, girdi izleme ve odak analiz modülü (Batuhan Sami)

## Notlar
- İlk çalıştırmada `face_landmarker.task` modeli otomatik indirilir (~30MB)
- Kalibrasyon ekranında 3 saniye düz oturup ekrana bakın
- Çıkmak için `q` tuşuna basın
