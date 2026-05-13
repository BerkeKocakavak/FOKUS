# FOKUS

FOKUS, bilgisayar başında çalışan kullanıcının odak durumunu kamera, klavye/fare aktivitesi ve açık uygulama bilgileriyle takip eden Windows masaüstü uygulamasıdır. Sistem; yüz, göz, bakış, postür ve kullanıcı girdilerini analiz ederek 0-100 aralığında bir odak puanı üretir, düşük odak durumlarında uyarı verir ve kullanıcı isterse kara listedeki uygulamalara müdahale eder.

Proje WPF tabanlı bir C# ana uygulama ve Python tabanlı bir kamera/analiz işçisinden oluşur. Ana ekran, kamera önizlemesini, odak puanını, anlık analiz değerlerini, cezaları, oturum özetlerini ve istatistikleri tek uygulama içinde gösterir.

## Proje Ekibi

- Batuhan Sami Akçay - Karar motoru, girdi izleme, IPC ve WPF uygulama akışı
- Berke Kocakavak - Bilgisayarlı görü, biyometrik analiz ve kalibrasyon
- Hasan Gürses - İşletim sistemi süreç yönetimi, müdahale ve otomasyon
- Türkay Aydoğan - Veri kaydı, analitik, istatistik ve gösterge paneli

## Temel Özellikler

- WPF masaüstü arayüzü
- Python kamera işçisini uygulama içinden otomatik başlatma ve durdurma
- WPF içinde canlı kamera önizlemesi
- Kalibrasyon sırasında yüz, göz, iris ve postür referans çizgileri
- Yüz var/yok, EAR, göz kırpma, bakış sapması ve postür analizi
- Klavye ve fare aktivitesinden tuş/dk, fare px/dk ve hareketsizlik takibi
- 0-100 arası odak puanı hesaplama
- Odak eşiği altına düşünce sesli uyarı ve popup bildirimi
- Uzun süre göz kapalı kalırsa uyuma uyarısı
- 15 saniye boş bakış algılanırsa sesli/popup uyarısı
- 4 dakika girdi alınmazsa "Çalışıyor musun?" bildirimi
- Ara ver/devam et modu
- Oturum sonunda verimlilik analizi
- SQLite tabanlı oturum, odak ölçümü, ceza ve kara liste kayıtları
- İstatistikler penceresinde odak trendi, günlük özetler, cezalar ve kara liste yakalamaları
- Kara liste uygulamalarını raporlama
- Kullanıcı açarsa kara listedeki uygulamaları askıya alma/devam ettirme
- Kritik Windows süreçlerini müdahaleden koruma
- Odak düşükken medya oynatmayı duraklatma, odak toparlanınca devam ettirme
- Sağ üstte küçük odak göstergesi
- Temel/gelişmiş ayrımlı ayarlar penceresi

## Teknoloji Yığını

- C# / WPF
- .NET 10
- Python 3.12
- OpenCV
- MediaPipe Face Landmarker
- pywin32 Named Pipes
- SQLite
- Microsoft.Data.Sqlite

## Sistem Gereksinimleri

- Windows 10 veya Windows 11
- .NET 10 SDK
- Python 3.12.x
- Kamera
- Python paketleri için internet erişimi
- İlk model indirme için internet erişimi

Önerilen Python sürümü: `Python 3.12.9`

## Kurulum

Repo klasörüne girin:

```powershell
cd path\to\FOKUS
```

Python sanal ortamı oluşturun:

```powershell
python -m venv venv
.\venv\Scripts\activate
```

Python paketlerini kurun:

```powershell
python -m pip install -r requirements.txt
```

.NET bağımlılıklarını geri yükleyip derlemeyi kontrol edin:

```powershell
dotnet build .\KararMotoru\KararMotoru.csproj
```

## Çalıştırma

En kolay yol:

```powershell
.\ÇALIŞTIR.bat
```

Alternatif olarak:

```powershell
dotnet run --project .\KararMotoru\KararMotoru.csproj
```

Uygulama açıldığında WPF ana pencere başlar, Python kamera işçisi arka planda çalıştırılır ve kamera görüntüsü WPF içinde gösterilir. Normal kullanımda ayrı OpenCV kamera penceresi açılmaz.

## İlk Kullanım

1. Uygulamayı başlatın.
2. Kamera izni gerektiğinde izin verin.
3. Kalibrasyon sırasında yüzünüzü kameraya alın.
4. Yaklaşık 3 saniye düz oturup ekrana bakın.
5. Kalibrasyon tamamlanınca odak puanı ve analiz değerleri canlı güncellenir.
6. Ara vermek istediğinizde `Ara ver` butonunu kullanın.
7. Oturumu bitirmek için `Durdur` butonuna basın.

## Arayüz

Ana ekran dört ana parçadan oluşur:

- Sol bölüm: canlı kamera görüntüsü
- Sağ bölüm: odak puanı, yüz/pipe/kalibrasyon durumu ve odak geçmiş grafiği
- Alt sekmeler: girdi ve analiz değerleri, cezalar, hata/müdahale bilgileri, oturum özeti
- Üst butonlar: başlat, durdur, ara ver, istatistikler, ayarlar

`İstatistikler` penceresinde SQLite veritabanından okunan geçmiş oturum verileri gösterilir. Grafikler ve özet değerler veritabanındaki aynı kaynaktan üretildiği için ana kayıtlarla tutarlıdır.

## Ayarlar

Ayarlar penceresi iki katmanlıdır.

Normal ayarlarda günlük kullanım için gereken alanlar bulunur:

- Odak eşiği
- Önizleme FPS
- Analiz FPS
- Boş bakış süresi
- Uyarı notu
- Kara liste müdahalesi
- Kara liste
- Beyaz liste

`Gelişmiş ayarları göster` seçeneğiyle hassas teknik ayarlar açılır:

- EMA yumuşatma
- Yüz yokken düşme hızı
- Pipe zaman aşımı
- Girdi örnekleme aralığı
- Aktivite penceresi
- Hareketsizlik uyarı süresi
- Beklenen tuş/dk ve fare px/dk
- Düşük aktivite ceza tavanı
- EAR, bakış ve postür eşik/katsayı/tavan değerleri

### Katsayı, Tavan ve Eşik Nedir?

- Eşik: Bir ölçümün ne zaman sorun sayılacağını belirleyen sınırdır. Örneğin bakış sapması eşikten büyükse bakış cezası oluşur.
- Katsayı: Sorunun puana ne kadar sert yansıyacağını belirler. Katsayı büyüdükçe aynı sapma daha yüksek ceza üretir.
- Tavan: Bir ceza türünün en fazla kaç puan düşürebileceğini sınırlar. Böylece tek bir ölçüm odak puanını aşırı düşürmez.

## Karar Motoru

Karar motoru C# tarafında çalışır. Python'dan gelen biyometrik verileri, klavye/fare aktivitesini ve süreç tarama sonucunu birlikte değerlendirir.

Odak puanı şu kaynaklardan etkilenir:

- Yüzün kamerada olup olmaması
- Göz açıklığı/EAR değeri
- Bakış sapması
- Postür sapması
- Boş bakış bayrağı
- Klavye/fare aktivite düşüklüğü

Kara listedeki uygulamalar artık doğrudan odak cezası üretmez. Kara liste, raporlama ve isteğe bağlı müdahale için kullanılır. Müdahale açıksa ve odak puanı eşik altındaysa kara listedeki uygulamalar askıya alınabilir.

## Kamera ve Kalibrasyon

Python tarafındaki `kamera_test.py` kamera görüntüsünü alır, MediaPipe Face Landmarker ile yüz noktalarını analiz eder ve sonuçları C# uygulamasına aktarır.

Kalibrasyon sırasında:

- Yüzün kadrajda ve yeterli büyüklükte olması beklenir.
- Gözlerin açık olması kontrol edilir.
- Başın fazla sağa/sola veya öne/arkaya eğilmemesi beklenir.
- Uygun örneklerden referans EAR, bakış ve postür değerleri alınır.
- Kamera görüntüsünde yüz çevresi, göz/iris noktaları ve baş referans çizgileri gösterilir.

Kalibrasyon tamamlandıktan sonra kamera görüntüsünde analiz yazıları gösterilmez; değerler WPF arayüzünde gösterilir.

## IPC Haberleşme

Python ve C# arasındaki veri aktarımı Windows Named Pipes üzerinden yapılır.

İki ayrı pipe kullanılır:

- Biyometrik veri pipe'ı: Python JSON satırları yazar, C# karar motoru okur.
- Kamera görüntü pipe'ı: Python JPEG kareleri uzunluk bilgisiyle gönderir, WPF önizleme okuyucusu gösterir.

Biyometrik paketler satır sonlu JSON formatındadır. Örnek alanlar:

```json
{
  "zaman": 1710000000.0,
  "kamera_bagli": true,
  "ear": 0.28,
  "ear_esik": 0.20,
  "gaze": 0.51,
  "gaze_sapma": 0.01,
  "one_sapma": 2.4,
  "yana_sapma": 1.1,
  "yuz_var": true,
  "gaze_yon": "MERKEZE BAKIYOR",
  "bas_durum": "BAS DUZGUN",
  "kalibrasyon_tamam": true
}
```

Projede ayrı bir handshake paketi yoktur. Bağlantı, Named Pipe `Connect` işlemiyle kurulur. Pipe veya kamera bağlantısı kesilirse C# tarafı durumu algılar, kullanıcı arayüzünü günceller ve yeniden bağlanma akışını başlatır.

## Süreç Müdahalesi

Kara liste müdahalesi varsayılan olarak kapalıdır. Kullanıcı ayarlardan açarsa karar motoru odak düşükken kara listedeki süreçlere müdahale edebilir.

Müdahale davranışı:

- Kara listedeki uygulamalar tespit edilir.
- Odak puanı eşik altına düşerse hedef uygulama önce görev çubuğuna küçültülür.
- Ardından süreç askıya alınır.
- Odak puanı tekrar eşik üstüne çıkarsa askıya alınan süreç devam ettirilir.
- Sistem uygulamayı otomatik büyütmez; kullanıcı isterse kendisi açar.

Kritik süreç koruması vardır. Windows ve uygulama için kritik görülen süreçlere müdahale edilmez, kullanıcıya uyarı gösterilir.

## Medya Kontrolü

Odak puanı eşik altına düştüğünde sistem açık medya oynatımını duraklatmayı dener. Odak puanı tekrar eşik üstüne çıktığında, sistemin duraklattığı medya için devam komutu gönderilir.

Bu özellik YouTube, müzik oynatıcıları ve medya tuşlarını destekleyen uygulamalarda çalışır. Her uygulamanın medya komutlarına verdiği tepki farklı olabilir.

## Veri Kaydı

Uygulama çalışma dosyalarını proje ana dizinine dağınık şekilde yazmaz. Çalışma sırasında aşağıdaki klasörler otomatik oluşur:

```text
ayarlar/
veriler/
durum/
loglar/
modeller/
```

Önemli dosyalar:

- `ayarlar/karar_motoru_ayarlar.json`: uygulama ayarları
- `veriler/fokus.db`: SQLite veritabanı
- `durum/aktif_odak.txt`: geriye uyumlu anlık odak çıktısı
- `durum/karar_motoru_durum.json`: geriye uyumlu durum çıktısı
- `loglar/camera_worker.log`: Python kamera işçisi logları
- `modeller/face_landmarker.task`: MediaPipe yüz modeli

SQLite tarafında temel veri grupları:

- Oturum kayıtları
- Odak örnekleri
- Ceza kayıtları
- Kara liste yakalamaları

## Proje Yapısı

```text
FOKUS/
├─ KararMotoru/
│  ├─ Models/                 # C# veri modelleri
│  ├─ Services/               # Karar motoru, DB, girdi izleme, süreç ve medya servisleri
│  ├─ Views/                  # WPF pencereleri
│  ├─ App.xaml
│  └─ KararMotoru.csproj
├─ kamera_test.py             # Python kamera ve biyometrik analiz işçisi
├─ bagimlilik_kontrol.py      # Python bağımlılık kontrolü
├─ requirements.txt           # Python paketleri
├─ ÇALIŞTIR.bat               # Hızlı başlatma dosyası
└─ README.md
```

## Önemli C# Bileşenleri

- `MainWindow`: ana WPF ekranı, başlat/durdur/ara ver akışı, uyarılar ve kamera önizlemesi
- `SettingsWindow`: temel/gelişmiş ayarlar ekranı
- `DashboardWindow`: geçmiş istatistikler ve grafikler
- `SessionAnalysisWindow`: oturum sonu verimlilik analizi
- `KararMotoruWorker`: Python IPC verisini karar motoru state'ine dönüştüren ana servis
- `PythonCameraWorker`: Python kamera sürecini başlatan, durduran ve görüntü pipe'ını okuyan servis
- `OdakPuaniMotoru`: odak puanı ve ceza hesaplama
- `GirdiIzleyici`: klavye/fare aktivite takibi
- `SurecTarayici`: kara/beyaz liste süreç tespiti
- `SurecYonetici`: süreç küçültme, askıya alma, devam ettirme ve sonlandırma
- `FokusDb`: SQLite şema, yazma ve okuma işlemleri
- `MedyaYonetici`: medya duraklat/devam komutları

## Test ve Doğrulama

Derleme:

```powershell
dotnet build .\KararMotoru\KararMotoru.csproj
```

Python söz dizimi kontrolü:

```powershell
python -m py_compile .\kamera_test.py .\bagimlilik_kontrol.py
```

Manuel test önerileri:

- Uygulama açıldığında Python kamera işçisi otomatik başlamalı.
- Kalibrasyon sırasında yüz/göz/iris çizgileri görünmeli.
- Kalibrasyon sonrası analiz yazıları kamera üzerinde değil, arayüzde görünmeli.
- Odak puanı 0-100 aralığında kalmalı.
- Göz kapalı, bakış sapması, postür sapması ve boş bakış durumlarında ceza oluşmalı.
- Klavye/fare aktivitesi tuş/dk ve fare px/dk alanlarını değiştirmeli.
- Odak eşiği altına düşünce sesli uyarı gelmeli.
- 15 saniye boş bakışta popup uyarı çıkmalı.
- 4 dakika girdi yoksa "Çalışıyor musun?" bildirimi açık kalmalı.
- Ara ver/devam et sırasında oturum süresi ara süresini saymamalı.
- Durdurunca kamera görüntüsü temizlenmeli.
- Kamera bağlantısı koparsa uygulama donmadan duraklatma moduna geçmeli.
- Kamera geri gelince kullanıcı devam ettiğinde akış toparlanmalı.
- Kara liste müdahalesi kapalıyken süreçlere dokunulmamalı.
- Kara liste müdahalesi açıkken hedef süreç önce küçültülüp sonra askıya alınmalı.
- Kritik süreçler korunmalı ve kullanıcıya uyarı gösterilmeli.
- İstatistikler penceresi veritabanındaki kayıtlarla tutarlı güncellenmeli.

## Yayınlama

EXE üretmek için:

```powershell
dotnet publish .\KararMotoru\KararMotoru.csproj -c Release -r win-x64 --self-contained false
```

Çıktı tipik olarak şu klasöre yazılır:

```text
KararMotoru/bin/Release/net10.0-windows10.0.17763.0/win-x64/publish/
```

Not: Bu proje Python kamera işçisine bağlıdır. Sadece C# uygulamasını EXE yapmak Python bağımlılıklarını ortadan kaldırmaz. Son dağıtımda şu seçeneklerden biri gerekir:

- Hedef bilgisayarda Python ve `requirements.txt` paketlerinin kurulu olması
- Python ortamını uygulamayla birlikte paketlemek
- Python işçisini ayrıca paketlenmiş bir executable haline getirmek

## Sorun Giderme

### Kamera açılmıyor

- Kamerayı başka uygulama kullanıyor olabilir.
- Windows kamera izinlerini kontrol edin.
- USB kamera kullanıyorsanız çıkarıp tekrar takın.

### Analiz modeli hatası

- `modeller/face_landmarker.task` dosyasını kontrol edin.
- İlk çalıştırmada internet bağlantısı gerektiğini unutmayın.
- Model bozuk indiyse `modeller/face_landmarker.task` dosyasını silip uygulamayı tekrar başlatın.

### Pipe hazır değil veya bağlantı bekleniyor

- Python kamera işçisinin çalışıp çalışmadığını kontrol edin.
- Uygulamayı kapatıp tekrar açmadan önce eski Python süreçlerinin kapanmış olduğundan emin olun.
- `loglar/camera_worker.log` dosyasını kontrol edin.

### Python paketleri eksik

```powershell
.\venv\Scripts\activate
python -m pip install -r requirements.txt
```

### Kara liste müdahalesi çalışmıyor

- Ayarlardan kara liste müdahalesinin açık olduğundan emin olun.
- Kara listedeki süreç adını `.exe` olmadan yazmayı deneyin.
- Kritik süreçlere müdahale bilinçli olarak engellenir.
- Bazı uygulamalar yönetici yetkisi veya özel koruma nedeniyle askıya alınamayabilir.

### İstatistikler boş görünüyor

- En az bir oturum başlatıp birkaç saniye çalıştırın.
- `Durdur` butonuyla oturumu bitirin.
- `veriler/fokus.db` dosyasının oluştuğunu kontrol edin.

## Güvenlik ve Gizlilik

FOKUS kamera görüntüsünü normal kullanımda dosyaya kaydetmez. Kamera kareleri WPF önizlemesi için pipe üzerinden anlık aktarılır. Veritabanına kaydedilen bilgiler odak puanı, biyometrik ölçüm özetleri, girdi aktivitesi, ceza kayıtları, oturum süreleri ve kara liste yakalamalarıdır.

Süreç müdahalesi kullanıcı kontrolündedir ve varsayılan olarak kapalıdır. Kritik süreçlere müdahale engellenir.

## Bilinen Sınırlar

- Sistem Windows odaklıdır.
- Kamera ve MediaPipe modeli olmadan biyometrik analiz çalışmaz.
- Medya duraklat/devam özelliği uygulamaların medya komutlarına verdiği desteğe bağlıdır.
- EXE çıktısı tek başına Python bağımlılıklarını paketlemez.
- Performans değerleri donanıma, kamera FPS değerine ve MediaPipe modelinin çalışma hızına bağlıdır.
