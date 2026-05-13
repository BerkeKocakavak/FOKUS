import cv2
import numpy as np
import argparse
import os
import time
import json
import threading
import atexit
import shutil
import subprocess
import win32pipe
import win32file
import pywintypes

SOL_GOZ = [362, 385, 387, 263, 373, 380]
SAG_GOZ = [33, 160, 158, 133, 153, 144]
SOL_IRIS = [474, 475, 476, 477]
SAG_IRIS = [469, 470, 471, 472]

BURUN = 1
CENE = 152
ALIN = 10
SOL_KULAK = 234
SAG_KULAK = 454

KIRPMA_KARE = 2
KALIBRASYON_SURE = 3
KALIBRASYON_MIN_ORNEK = 12
KALIBRASYON_MIN_EAR = 0.16
PIPE_ADI = r'\\.\pipe\fokus_pipe'
FRAME_PIPE_ADI = None
KOK_DIZIN = os.path.dirname(os.path.abspath(__file__))
LOG_DIZIN = os.environ.get("FOKUS_LOG_DIR") or os.path.join(KOK_DIZIN, "loglar")
MODEL_DIZIN = os.environ.get("FOKUS_MODEL_DIR") or os.path.join(KOK_DIZIN, "modeller")
KARAR_MOTORU_PROJE = os.path.join(KOK_DIZIN, "KararMotoru", "KararMotoru.csproj")
KARAR_MOTORU_LOG = os.path.join(LOG_DIZIN, "karar_motoru.log")
MODEL_PATH = os.path.join(MODEL_DIZIN, "face_landmarker.task")
MODEL_ASSET_PATH = os.path.join("modeller", "face_landmarker.task")
HEADLESS = False
FRAME_INTERVAL = 1 / 30
ANALIZ_INTERVAL = 1 / 10
KAMERA_YENIDEN_DENEME_ARALIGI = 0.5
son_frame_yazma = 0.0
son_analiz_zamani = 0.0
son_kamera_log_zamani = 0.0
son_result = None
detector = None
detector_hazir = False
detector_hatasi = None
mp = None
python = None
vision = None

# Kalibrasyon değerleri
kalibrasyon_tamam = False
ref_ear = None
ref_gaze = None
ref_one = None
ref_yana = None
ref_gaze_esik = 0.02

kirpma_sayaci = 0
toplam_kirpma = 0

kal_ear_listesi = []
kal_gaze_listesi = []
kal_one_listesi = []
kal_yana_listesi = []
kal_gecerli_sure = 0.0
kal_son_ornek_zamani = None

# Paylaşılan veri
paylasilan_veri = {}
veri_kilidi = threading.Lock()
pipe_bagli = False
frame_pipe = None
frame_pipe_bagli = False
frame_pipe_kilidi = threading.Lock()
karar_motoru_proc = None
karar_motoru_log = None

os.chdir(KOK_DIZIN)

for klasor in (LOG_DIZIN, MODEL_DIZIN):
    os.makedirs(klasor, exist_ok=True)

def argumanlari_oku():
    parser = argparse.ArgumentParser(description="FOKUS kamera ve pipe isçisi")
    parser.add_argument("--headless", action="store_true", help="OpenCV penceresi acmadan calisir.")
    parser.add_argument("--pipe-name", default="fokus_pipe", help="Named pipe adi veya tam pipe yolu.")
    parser.add_argument("--frame-pipe-name", help="WPF kamera onizlemesi icin named pipe adi veya tam pipe yolu.")
    parser.add_argument("--preview-fps", type=float, default=30, help="WPF onizleme JPEG yazma hizi.")
    parser.add_argument("--analysis-fps", type=float, default=10, help="MediaPipe analiz hizi.")
    return parser.parse_args()

def frame_yaz(frame):
    global son_frame_yazma

    if not FRAME_PIPE_ADI:
        return

    simdi = time.time()
    if simdi - son_frame_yazma < FRAME_INTERVAL:
        return

    basarili, buffer = cv2.imencode(".jpg", frame, [int(cv2.IMWRITE_JPEG_QUALITY), 75])
    if not basarili:
        return

    jpeg_bytes = buffer.tobytes()
    frame_pipe_gonder(jpeg_bytes)
    son_frame_yazma = simdi

def frame_pipe_gonder(jpeg_bytes):
    global frame_pipe, frame_pipe_bagli

    if not FRAME_PIPE_ADI:
        return False

    with frame_pipe_kilidi:
        pipe = frame_pipe

    if pipe is None:
        return False

    try:
        mesaj = len(jpeg_bytes).to_bytes(4, byteorder="little", signed=False) + jpeg_bytes
        win32file.WriteFile(pipe, mesaj)
        return True
    except pywintypes.error:
        print("Kamera goruntu pipe baglantisi kesildi, yeniden bekleniyor...")
    except Exception as e:
        print(f"Kamera goruntu pipe hatasi: {e}")

    with frame_pipe_kilidi:
        if frame_pipe == pipe:
            frame_pipe = None
            frame_pipe_bagli = False

    try:
        win32file.CloseHandle(pipe)
    except Exception:
        pass

    return False

def analiz_modelini_yukle():
    global detector, detector_hazir, detector_hatasi, mp, python, vision

    try:
        import mediapipe as mp_mod
        from mediapipe.tasks import python as mp_python
        from mediapipe.tasks.python import vision as mp_vision

        mp = mp_mod
        python = mp_python
        vision = mp_vision

        model_path = MODEL_PATH
        if not os.path.exists(model_path):
            import urllib.request
            print("Model indiriliyor...")
            urllib.request.urlretrieve(
                "https://storage.googleapis.com/mediapipe-models/face_landmarker/face_landmarker/float16/1/face_landmarker.task",
                model_path
            )

        base_options = python.BaseOptions(model_asset_path=MODEL_ASSET_PATH)
        options = vision.FaceLandmarkerOptions(
            base_options=base_options,
            num_faces=1,
            min_face_detection_confidence=0.5,
            min_face_presence_confidence=0.5,
            min_tracking_confidence=0.5
        )

        detector = vision.FaceLandmarker.create_from_options(options)
        detector_hazir = True
        print("Analiz modeli hazir.")
    except Exception as e:
        detector_hatasi = str(e)
        print(f"Analiz modeli yuklenemedi: {e}")

def karar_motoru_zaten_calisiyor():
    try:
        cikti = subprocess.check_output(
            ["tasklist", "/FI", "IMAGENAME eq KararMotoru.exe", "/NH"],
            text=True,
            encoding="utf-8",
            errors="ignore"
        )
        return "KararMotoru.exe" in cikti
    except Exception:
        return False

def karar_motoru_baslat():
    global karar_motoru_proc, karar_motoru_log

    if os.environ.get("FOKUS_KARAR_MOTORU_OTOMATIK", "0") != "1":
        print("Karar motoru otomatik baslatma kapali.")
        return None

    if karar_motoru_zaten_calisiyor():
        print("Karar motoru zaten calisiyor, mevcut surece baglanilacak.")
        return None

    dotnet = shutil.which("dotnet")
    if dotnet is None:
        print("dotnet bulunamadi. Karar motorunu otomatik baslatamiyorum.")
        return None

    if not os.path.exists(KARAR_MOTORU_PROJE):
        print(f"Karar motoru projesi bulunamadi: {KARAR_MOTORU_PROJE}")
        return None

    try:
        karar_motoru_log = open(KARAR_MOTORU_LOG, "w", encoding="utf-8")
        creationflags = subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0
        karar_motoru_proc = subprocess.Popen(
            [dotnet, "run", "--project", KARAR_MOTORU_PROJE],
            cwd=KOK_DIZIN,
            stdout=karar_motoru_log,
            stderr=subprocess.STDOUT,
            creationflags=creationflags
        )
        print("Karar motoru otomatik baslatildi.")
        print(f"Karar motoru log dosyasi: {KARAR_MOTORU_LOG}")
        return karar_motoru_proc
    except Exception as e:
        print(f"Karar motoru baslatilamadi: {e}")
        if karar_motoru_log:
            karar_motoru_log.close()
            karar_motoru_log = None
        return None

def karar_motoru_durdur():
    global karar_motoru_proc, karar_motoru_log

    if karar_motoru_proc is not None and karar_motoru_proc.poll() is None:
        karar_motoru_proc.terminate()
        try:
            karar_motoru_proc.wait(timeout=3)
        except subprocess.TimeoutExpired:
            karar_motoru_proc.kill()

    karar_motoru_proc = None

    if karar_motoru_log is not None:
        karar_motoru_log.close()
        karar_motoru_log = None

atexit.register(karar_motoru_durdur)

def pipe_sunucusu():
    global pipe_bagli
    print("Pipe sunucusu baslatılıyor...")
    while True:
        pipe = None
        try:
            pipe = win32pipe.CreateNamedPipe(
                PIPE_ADI,
                win32pipe.PIPE_ACCESS_OUTBOUND,
                win32pipe.PIPE_TYPE_MESSAGE | win32pipe.PIPE_WAIT,
                1, 65536, 65536, 0, None
            )
            print("C# istemcisi bekleniyor...")
            try:
                win32pipe.ConnectNamedPipe(pipe, None)
            except pywintypes.error as e:
                hata_kodu = getattr(e, "winerror", e.args[0] if e.args else None)
                if hata_kodu != 535:  # ERROR_PIPE_CONNECTED
                    raise

            pipe_bagli = True
            print("C# baglandi!")

            while True:
                with veri_kilidi:
                    veri = paylasilan_veri.copy()

                if veri:
                    mesaj = json.dumps(veri) + "\n"
                    try:
                        win32file.WriteFile(pipe, mesaj.encode('utf-8'))
                    except pywintypes.error:
                        print("Baglanti kesildi, yeniden bekleniyor...")
                        break

                time.sleep(0.1)

        except Exception as e:
            print(f"Pipe hatasi: {e}")
        finally:
            pipe_bagli = False
            if pipe is not None:
                try:
                    win32file.CloseHandle(pipe)
                except Exception:
                    pass
            time.sleep(1)

def frame_pipe_sunucusu():
    global frame_pipe, frame_pipe_bagli

    if not FRAME_PIPE_ADI:
        return

    print("Kamera goruntu pipe sunucusu baslatiliyor...")
    while True:
        pipe = None
        try:
            pipe = win32pipe.CreateNamedPipe(
                FRAME_PIPE_ADI,
                win32pipe.PIPE_ACCESS_OUTBOUND,
                win32pipe.PIPE_TYPE_BYTE | win32pipe.PIPE_WAIT,
                1, 1048576, 1048576, 0, None
            )
            print("WPF kamera istemcisi bekleniyor...")
            try:
                win32pipe.ConnectNamedPipe(pipe, None)
            except pywintypes.error as e:
                hata_kodu = getattr(e, "winerror", e.args[0] if e.args else None)
                if hata_kodu != 535:  # ERROR_PIPE_CONNECTED
                    raise

            with frame_pipe_kilidi:
                frame_pipe = pipe
                frame_pipe_bagli = True

            print("WPF kamera istemcisi baglandi!")
            while True:
                with frame_pipe_kilidi:
                    bagli = frame_pipe_bagli and frame_pipe == pipe
                if not bagli:
                    break
                time.sleep(0.2)

        except Exception as e:
            print(f"Kamera goruntu pipe hatasi: {e}")
        finally:
            with frame_pipe_kilidi:
                if frame_pipe == pipe:
                    frame_pipe = None
                    frame_pipe_bagli = False

            if pipe is not None:
                try:
                    win32file.CloseHandle(pipe)
                except Exception:
                    pass
            time.sleep(0.5)


def kamera_ac():
    cap = cv2.VideoCapture(0, cv2.CAP_DSHOW) if os.name == "nt" else cv2.VideoCapture(0)
    cap.set(cv2.CAP_PROP_FRAME_WIDTH, 640)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 480)
    cap.set(cv2.CAP_PROP_FPS, 30)
    cap.set(cv2.CAP_PROP_BUFFERSIZE, 1)

    if cap.isOpened():
        return cap

    cap.release()
    return None


def kamera_durumunu_yayimla(mesaj):
    global son_result
    son_result = None
    with veri_kilidi:
        paylasilan_veri.update({
            "zaman": time.time(),
            "kamera_bagli": False,
            "yuz_var": False,
            "analiz_hazir": False,
            "analiz_durumu": mesaj,
            "kalibrasyon_tamam": kalibrasyon_tamam,
            "kalibrasyon_kalan_saniye": 0,
            "gaze_yon": "YOK",
            "bas_durum": "YOK"
        })


def ear_hesapla(landmarks, goz_noktalari, w, h):
    nokta = lambda i: np.array([landmarks[i].x * w, landmarks[i].y * h])
    p1, p2, p3, p4, p5, p6 = [nokta(i) for i in goz_noktalari]
    dikey1 = np.linalg.norm(p2 - p6)
    dikey2 = np.linalg.norm(p3 - p5)
    yatay = np.linalg.norm(p1 - p4)
    return (dikey1 + dikey2) / (2.0 * yatay)

def iris_merkez(landmarks, iris_noktalari, w, h):
    x = np.mean([landmarks[i].x for i in iris_noktalari]) * w
    y = np.mean([landmarks[i].y for i in iris_noktalari]) * h
    return np.array([x, y])

def gaze_hesapla(landmarks, w, h):
    sol_iris = iris_merkez(landmarks, SOL_IRIS, w, h)
    sol_ic = np.array([landmarks[SOL_GOZ[0]].x * w, landmarks[SOL_GOZ[0]].y * h])
    sol_dis = np.array([landmarks[SOL_GOZ[3]].x * w, landmarks[SOL_GOZ[3]].y * h])
    sol_genislik = np.linalg.norm(sol_dis - sol_ic)
    sol_oran = np.linalg.norm(sol_iris - sol_ic) / sol_genislik if sol_genislik > 0 else 0.5

    sag_iris = iris_merkez(landmarks, SAG_IRIS, w, h)
    sag_ic = np.array([landmarks[SAG_GOZ[0]].x * w, landmarks[SAG_GOZ[0]].y * h])
    sag_dis = np.array([landmarks[SAG_GOZ[3]].x * w, landmarks[SAG_GOZ[3]].y * h])
    sag_genislik = np.linalg.norm(sag_dis - sag_ic)
    sag_oran = np.linalg.norm(sag_iris - sag_ic) / sag_genislik if sag_genislik > 0 else 0.5

    return (sol_oran + sag_oran) / 2.0

def bas_egimi_hesapla(landmarks, w, h):
    burun = np.array([landmarks[BURUN].x * w, landmarks[BURUN].y * h])
    cene = np.array([landmarks[CENE].x * w, landmarks[CENE].y * h])
    alin = np.array([landmarks[ALIN].x * w, landmarks[ALIN].y * h])
    sol_kulak = np.array([landmarks[SOL_KULAK].x * w, landmarks[SOL_KULAK].y * h])
    sag_kulak = np.array([landmarks[SAG_KULAK].x * w, landmarks[SAG_KULAK].y * h])

    merkez_y = (alin[1] + cene[1]) / 2
    one_egim = burun[1] - merkez_y
    yana_egim = sag_kulak[1] - sol_kulak[1]

    return one_egim, yana_egim


def landmark_noktasi(landmark, w, h, aynali=True):
    x = (1.0 - landmark.x) * w if aynali else landmark.x * w
    y = landmark.y * h
    return int(np.clip(x, 0, w - 1)), int(np.clip(y, 0, h - 1))


def landmark_cizgisi_ciz(frame, landmarks, noktalar, w, h, renk, kapali=False, kalinlik=1):
    pts = np.array([landmark_noktasi(landmarks[i], w, h) for i in noktalar], dtype=np.int32)
    cv2.polylines(frame, [pts], kapali, renk, kalinlik, cv2.LINE_AA)


def kalibrasyon_cizimleri_ciz(frame, landmarks, w, h, uygun):
    nokta_renk = (80, 180, 255) if uygun else (0, 180, 255)
    ana_renk = (0, 220, 120) if uygun else (0, 200, 255)

    tum_noktalar = np.array(
        [landmark_noktasi(landmark, w, h) for landmark in landmarks],
        dtype=np.int32
    )
    if len(tum_noktalar) >= 3:
        hull = cv2.convexHull(tum_noktalar)
        cv2.polylines(frame, [hull], True, ana_renk, 2, cv2.LINE_AA)

    for i in range(0, len(landmarks), 6):
        cv2.circle(frame, landmark_noktasi(landmarks[i], w, h), 1, nokta_renk, -1, cv2.LINE_AA)

    landmark_cizgisi_ciz(frame, landmarks, SOL_GOZ, w, h, (0, 255, 255), kapali=True, kalinlik=2)
    landmark_cizgisi_ciz(frame, landmarks, SAG_GOZ, w, h, (0, 255, 255), kapali=True, kalinlik=2)
    landmark_cizgisi_ciz(frame, landmarks, SOL_IRIS, w, h, (255, 180, 0), kapali=True, kalinlik=2)
    landmark_cizgisi_ciz(frame, landmarks, SAG_IRIS, w, h, (255, 180, 0), kapali=True, kalinlik=2)

    for i in SOL_GOZ + SAG_GOZ + SOL_IRIS + SAG_IRIS:
        cv2.circle(frame, landmark_noktasi(landmarks[i], w, h), 3, (0, 255, 255), -1, cv2.LINE_AA)

    landmark_cizgisi_ciz(frame, landmarks, [ALIN, BURUN, CENE], w, h, (255, 255, 255), kalinlik=2)
    landmark_cizgisi_ciz(frame, landmarks, [SOL_KULAK, BURUN, SAG_KULAK], w, h, (255, 255, 255), kalinlik=2)
    cv2.circle(frame, landmark_noktasi(landmarks[BURUN], w, h), 4, (0, 0, 255), -1, cv2.LINE_AA)


def kalibrasyon_kalitesi(landmarks, w, h, ear, one_egim, yana_egim):
    xs = np.array([landmark.x * w for landmark in landmarks])
    ys = np.array([landmark.y * h for landmark in landmarks])
    yuz_genislik = float(xs.max() - xs.min())
    yuz_yukseklik = float(ys.max() - ys.min())
    merkez_x = float((xs.max() + xs.min()) / 2.0)
    merkez_y = float((ys.max() + ys.min()) / 2.0)

    if yuz_genislik < w * 0.18 or yuz_yukseklik < h * 0.24:
        return False, "Yuzunu kameraya biraz yaklastir"

    if merkez_x < w * 0.30 or merkez_x > w * 0.70 or merkez_y < h * 0.20 or merkez_y > h * 0.82:
        return False, "Yuzunu kadrajin ortasina al"

    if ear < KALIBRASYON_MIN_EAR:
        return False, "Gozlerini acik tut"

    if abs(yana_egim) > yuz_yukseklik * 0.16:
        return False, "Basini yana egme"

    if abs(one_egim) > yuz_yukseklik * 0.30:
        return False, "Basini dik tut"

    return True, "Uygun"


def kalibrasyon_medyani(degerler, varsayilan=0.0):
    return float(np.median(degerler)) if degerler else varsayilan


# Önce kütüphaneyi kur
try:
    import win32pipe
except ImportError:
    print("pywin32 kuruluyor...")
    os.system("pip install pywin32")
    import win32pipe

args = argumanlari_oku()
HEADLESS = args.headless
PIPE_ADI = args.pipe_name if args.pipe_name.startswith("\\\\.\\pipe\\") else rf'\\.\pipe\{args.pipe_name}'
FRAME_PIPE_ADI = (
    args.frame_pipe_name if args.frame_pipe_name and args.frame_pipe_name.startswith("\\\\.\\pipe\\")
    else (rf'\\.\pipe\{args.frame_pipe_name}' if args.frame_pipe_name else None)
)
FRAME_INTERVAL = 1 / max(args.preview_fps, 1)
ANALIZ_INTERVAL = 1 / max(args.analysis_fps, 1)
if HEADLESS:
    print("Headless kamera modu aktif.")
print(f"Pipe adi: {PIPE_ADI}")
if FRAME_PIPE_ADI:
    print(f"Kamera goruntu pipe adi: {FRAME_PIPE_ADI}")
print(f"Onizleme FPS: {args.preview_fps:.0f}, analiz FPS: {args.analysis_fps:.0f}")

# Pipe sunucusunu model/kamera yuklenmeden once ac. C# istemcisi erken baglanabilsin.
pipe_thread = threading.Thread(target=pipe_sunucusu, daemon=True)
pipe_thread.start()
if FRAME_PIPE_ADI:
    frame_pipe_thread = threading.Thread(target=frame_pipe_sunucusu, daemon=True)
    frame_pipe_thread.start()

cap = kamera_ac()
if cap is None:
    print("Kamera acilamadi. Kamera izinlerini ve baska uygulama kullanip kullanmadigini kontrol edin.")
    kamera_durumunu_yayimla("Kamera acilamadi")

model_thread = threading.Thread(target=analiz_modelini_yukle, daemon=True)
model_thread.start()

karar_motoru_baslat()

while True:
    if cap is None:
        kamera_durumunu_yayimla("Kamera baglantisi yok")
        time.sleep(KAMERA_YENIDEN_DENEME_ARALIGI)
        cap = kamera_ac()
        if cap is not None:
            print("Kamera yeniden baglandi.")
        continue

    try:
        ret, frame = cap.read()
    except cv2.error as e:
        ret, frame = False, None
        if time.time() - son_kamera_log_zamani >= 1:
            print(f"Kamera okuma hatasi: {e}")
            son_kamera_log_zamani = time.time()

    if not ret or frame is None:
        if time.time() - son_kamera_log_zamani >= 1:
            print("Kamera baglantisi koptu, yeniden baglanma deneniyor...")
            son_kamera_log_zamani = time.time()
        kamera_durumunu_yayimla("Kamera baglantisi koptu")
        cap.release()
        cap = None
        time.sleep(KAMERA_YENIDEN_DENEME_ARALIGI)
        continue

    h, w, _ = frame.shape
    simdi = time.time()
    analiz_yeni = False
    if detector_hazir and detector is not None and simdi - son_analiz_zamani >= ANALIZ_INTERVAL:
        rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
        son_result = detector.detect(mp_image)
        son_analiz_zamani = simdi
        analiz_yeni = True

    result = son_result
    frame = cv2.flip(frame, 1)

    if not detector_hazir:
        mesaj = "Analiz yukleniyor..."
        if detector_hatasi:
            mesaj = "Analiz modeli hatasi"
        with veri_kilidi:
            paylasilan_veri.update({
                "zaman": time.time(),
                "kamera_bagli": True,
                "yuz_var": False,
                "analiz_hazir": False,
                "analiz_durumu": mesaj,
                "kalibrasyon_tamam": False,
                "kalibrasyon_kalan_saniye": 0
            })

    elif result and result.face_landmarks:
        landmarks = result.face_landmarks[0]

        sol_ear = ear_hesapla(landmarks, SOL_GOZ, w, h)
        sag_ear = ear_hesapla(landmarks, SAG_GOZ, w, h)
        ear = (sol_ear + sag_ear) / 2.0
        gaze = gaze_hesapla(landmarks, w, h)
        one_egim, yana_egim = bas_egimi_hesapla(landmarks, w, h)

        if not kalibrasyon_tamam:
            uygun, kalite_mesaj = kalibrasyon_kalitesi(landmarks, w, h, ear, one_egim, yana_egim)
            kalibrasyon_cizimleri_ciz(frame, landmarks, w, h, uygun)

            if analiz_yeni and uygun:
                if kal_son_ornek_zamani is None:
                    kal_son_ornek_zamani = simdi

                gecen = min(max(simdi - kal_son_ornek_zamani, 0.0), 0.25)
                kal_son_ornek_zamani = simdi
                kal_gecerli_sure += gecen
                kal_ear_listesi.append(ear)
                kal_gaze_listesi.append(gaze)
                kal_one_listesi.append(one_egim)
                kal_yana_listesi.append(yana_egim)
            elif analiz_yeni:
                kal_son_ornek_zamani = None

            kalan = int(np.ceil(max(KALIBRASYON_SURE - kal_gecerli_sure, 0.0)))
            gerekli_ornek = max(3, min(KALIBRASYON_MIN_ORNEK, int((KALIBRASYON_SURE / ANALIZ_INTERVAL) * 0.6)))
            durum_metni = "Duz oturun, ekrana bakin" if uygun else kalite_mesaj
            durum_renk = (0, 255, 0) if uygun else (0, 200, 255)

            cv2.putText(frame, "KALIBRASYON", (w // 2 - 120, h // 2 - 60),
                        cv2.FONT_HERSHEY_SIMPLEX, 1.2, (0, 255, 255), 3)
            cv2.putText(frame, durum_metni, (w // 2 - 230, h // 2),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.72, durum_renk, 2)
            cv2.putText(frame, f"{max(kalan, 0)} saniye", (w // 2 - 70, h // 2 + 50),
                        cv2.FONT_HERSHEY_SIMPLEX, 1.0, (0, 255, 0), 2)
            cv2.putText(frame, f"Ornek: {len(kal_ear_listesi)}/{gerekli_ornek}", (24, 34),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.65, (255, 255, 255), 2)

            with veri_kilidi:
                paylasilan_veri.update({
                    "zaman": time.time(),
                    "kamera_bagli": True,
                    "ear": round(ear, 3),
                    "ear_esik": round(ref_ear * 0.75 if ref_ear else 0, 3),
                    "gaze": round(gaze, 3),
                    "gaze_sapma": 0,
                    "one_sapma": 0,
                    "yana_sapma": 0,
                    "kirpma_sayisi": toplam_kirpma,
                    "yuz_var": True,
                    "gaze_yon": "KALIBRASYON",
                    "bas_durum": "KALIBRASYON",
                    "analiz_hazir": True,
                    "analiz_durumu": "Kalibrasyon" if uygun else "Kalibrasyon bekliyor: " + kalite_mesaj,
                    "kalibrasyon_tamam": False,
                    "kalibrasyon_kalan_saniye": max(kalan, 0)
                })

            if kal_gecerli_sure >= KALIBRASYON_SURE and len(kal_ear_listesi) >= gerekli_ornek:
                ref_ear = kalibrasyon_medyani(kal_ear_listesi)
                ref_gaze = kalibrasyon_medyani(kal_gaze_listesi)
                ref_one = kalibrasyon_medyani(kal_one_listesi)
                ref_yana = kalibrasyon_medyani(kal_yana_listesi)
                gaze_std = float(np.std(kal_gaze_listesi)) if len(kal_gaze_listesi) > 1 else 0.0
                ref_gaze_esik = float(np.clip(max(0.015, gaze_std * 3.0), 0.015, 0.06))
                kalibrasyon_tamam = True
                print(f"Kalibrasyon tamam! EAR:{ref_ear:.2f} Gaze:{ref_gaze:.2f} GazeEsik:{ref_gaze_esik:.3f} Ornek:{len(kal_ear_listesi)}")

        else:
            ear_esik = ref_ear * 0.75

            if ear < ear_esik:
                kirpma_sayaci += 1
            else:
                if kirpma_sayaci >= KIRPMA_KARE:
                    toplam_kirpma += 1
                kirpma_sayaci = 0

            gaze_sapma = gaze - ref_gaze
            if gaze_sapma > ref_gaze_esik:
                gaze_yon = "SOLA BAKIYOR"
            elif gaze_sapma < -ref_gaze_esik:
                gaze_yon = "SAGA BAKIYOR"
            else:
                gaze_yon = "MERKEZE BAKIYOR"

            one_sapma = one_egim - ref_one
            yana_sapma = yana_egim - ref_yana

            if one_sapma > 15:
                bas_durum = "BAS ONE EGILMIS"
            elif one_sapma < -8:
                bas_durum = "BAS ARKAYA EGILMIS"
            elif yana_sapma > 15:
                bas_durum = "BAS SOLA YATIYOR"
            elif yana_sapma < -15:
                bas_durum = "BAS SAGA YATIYOR"
            else:
                bas_durum = "BAS DUZGUN"

            # Verileri IPC için hazırla
            with veri_kilidi:
                paylasilan_veri.update({
                    "zaman": time.time(),
                    "kamera_bagli": True,
                    "ear": round(ear, 3),
                    "ear_esik": round(ear_esik, 3),
                    "gaze": round(gaze, 3),
                    "gaze_sapma": round(gaze_sapma, 3),
                    "one_sapma": round(one_sapma, 1),
                    "yana_sapma": round(yana_sapma, 1),
                    "kirpma_sayisi": toplam_kirpma,
                    "yuz_var": True,
                    "gaze_yon": gaze_yon,
                    "bas_durum": bas_durum,
                    "analiz_hazir": True,
                    "analiz_durumu": "Analiz aktif",
                    "kalibrasyon_tamam": True,
                    "kalibrasyon_kalan_saniye": 0
                })

    else:
        if not kalibrasyon_tamam:
            kal_son_ornek_zamani = None
            kalan = int(np.ceil(max(KALIBRASYON_SURE - kal_gecerli_sure, 0.0)))
            cv2.putText(frame, "KALIBRASYON", (w // 2 - 120, h // 2 - 60),
                        cv2.FONT_HERSHEY_SIMPLEX, 1.2, (0, 255, 255), 3)
            cv2.putText(frame, "Yuz bulunamadi", (w // 2 - 140, h // 2),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.8, (0, 200, 255), 2)
            cv2.putText(frame, f"{max(kalan, 0)} saniye", (w // 2 - 70, h // 2 + 50),
                        cv2.FONT_HERSHEY_SIMPLEX, 1.0, (0, 255, 0), 2)

        with veri_kilidi:
            paylasilan_veri.update({
                "yuz_var": False,
                "zaman": time.time(),
                "kamera_bagli": True,
                "analiz_hazir": True,
                "analiz_durumu": "Kalibrasyon bekliyor: Yuz bulunamadi" if not kalibrasyon_tamam else "Yuz bulunamadi",
                "kalibrasyon_tamam": kalibrasyon_tamam,
                "kalibrasyon_kalan_saniye": max(kalan, 0) if not kalibrasyon_tamam else 0,
                "gaze_yon": "YOK",
                "bas_durum": "YOK"
            })

    frame_yaz(frame)

    if not HEADLESS:
        cv2.imshow("FOKUS - Postur Analizi", frame)
        if cv2.waitKey(1) & 0xFF == ord('q'):
            break

if cap is not None:
    cap.release()
if not HEADLESS:
    cv2.destroyAllWindows()
karar_motoru_durdur()
