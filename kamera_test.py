import cv2
import mediapipe as mp
from mediapipe.tasks import python
from mediapipe.tasks.python import vision
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
PIPE_ADI = r'\\.\pipe\fokus_pipe'
KOK_DIZIN = os.path.dirname(os.path.abspath(__file__))
KARAR_MOTORU_PROJE = os.path.join(KOK_DIZIN, "KararMotoru", "KararMotoru.csproj")
KARAR_MOTORU_LOG = os.path.join(KOK_DIZIN, "karar_motoru.log")
HEADLESS = False
FRAME_OUTPUT = None
FRAME_INTERVAL = 1 / 30
ANALIZ_INTERVAL = 1 / 10
son_frame_yazma = 0.0
son_analiz_zamani = 0.0
son_result = None

# Kalibrasyon değerleri
kalibrasyon_tamam = False
ref_ear = None
ref_gaze = None
ref_one = None
ref_yana = None

kirpma_sayaci = 0
toplam_kirpma = 0

kal_ear_listesi = []
kal_gaze_listesi = []
kal_one_listesi = []
kal_yana_listesi = []
kal_baslangic = None

# Paylaşılan veri
paylasilan_veri = {}
veri_kilidi = threading.Lock()
pipe_bagli = False
karar_motoru_proc = None
karar_motoru_log = None

def argumanlari_oku():
    parser = argparse.ArgumentParser(description="FOKUS kamera ve pipe isçisi")
    parser.add_argument("--headless", action="store_true", help="OpenCV penceresi acmadan calisir.")
    parser.add_argument("--frame-output", help="Son kamera karesini atomik JPEG olarak bu yola yazar.")
    parser.add_argument("--pipe-name", default="fokus_pipe", help="Named pipe adi veya tam pipe yolu.")
    parser.add_argument("--preview-fps", type=float, default=30, help="WPF onizleme JPEG yazma hizi.")
    parser.add_argument("--analysis-fps", type=float, default=10, help="MediaPipe analiz hizi.")
    return parser.parse_args()

def frame_yaz(frame):
    global son_frame_yazma

    if not FRAME_OUTPUT:
        return

    simdi = time.time()
    if simdi - son_frame_yazma < FRAME_INTERVAL:
        return

    tmp_yol = f"{FRAME_OUTPUT}.{os.getpid()}.tmp"
    try:
        klasor = os.path.dirname(os.path.abspath(FRAME_OUTPUT))
        if klasor:
            os.makedirs(klasor, exist_ok=True)

        basarili, buffer = cv2.imencode(".jpg", frame, [int(cv2.IMWRITE_JPEG_QUALITY), 75])
        if not basarili:
            return

        with open(tmp_yol, "wb") as dosya:
            dosya.write(buffer.tobytes())
        os.replace(tmp_yol, FRAME_OUTPUT)
        son_frame_yazma = simdi
    except Exception as e:
        print(f"Kamera karesi yazilamadi: {e}")
        try:
            if os.path.exists(tmp_yol):
                os.remove(tmp_yol)
        except Exception:
            pass

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

# Önce kütüphaneyi kur
try:
    import win32pipe
except ImportError:
    print("pywin32 kuruluyor...")
    os.system("pip install pywin32")
    import win32pipe

args = argumanlari_oku()
HEADLESS = args.headless
FRAME_OUTPUT = args.frame_output
PIPE_ADI = args.pipe_name if args.pipe_name.startswith("\\\\.\\pipe\\") else rf'\\.\pipe\{args.pipe_name}'
FRAME_INTERVAL = 1 / max(args.preview_fps, 1)
ANALIZ_INTERVAL = 1 / max(args.analysis_fps, 1)
if HEADLESS:
    print("Headless kamera modu aktif.")
if FRAME_OUTPUT:
    print(f"Kamera karesi yazilacak: {FRAME_OUTPUT}")
print(f"Pipe adi: {PIPE_ADI}")
print(f"Onizleme FPS: {args.preview_fps:.0f}, analiz FPS: {args.analysis_fps:.0f}")

# Pipe sunucusunu model/kamera yuklenmeden once ac. C# istemcisi erken baglanabilsin.
pipe_thread = threading.Thread(target=pipe_sunucusu, daemon=True)
pipe_thread.start()

model_path = "face_landmarker.task"
if not os.path.exists(model_path):
    import urllib.request
    print("Model indiriliyor...")
    urllib.request.urlretrieve(
        "https://storage.googleapis.com/mediapipe-models/face_landmarker/face_landmarker/float16/1/face_landmarker.task",
        model_path
    )

base_options = python.BaseOptions(model_asset_path=model_path)
options = vision.FaceLandmarkerOptions(
    base_options=base_options,
    num_faces=1,
    min_face_detection_confidence=0.5,
    min_face_presence_confidence=0.5,
    min_tracking_confidence=0.5
)

detector = vision.FaceLandmarker.create_from_options(options)
cap = cv2.VideoCapture(0)
cap.set(cv2.CAP_PROP_FRAME_WIDTH, 640)
cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 480)
cap.set(cv2.CAP_PROP_FPS, 30)

karar_motoru_baslat()

while True:
    ret, frame = cap.read()
    if not ret:
        break

    h, w, _ = frame.shape
    simdi = time.time()
    if simdi - son_analiz_zamani >= ANALIZ_INTERVAL:
        rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
        son_result = detector.detect(mp_image)
        son_analiz_zamani = simdi

    result = son_result
    frame = cv2.flip(frame, 1)

    if result and result.face_landmarks:
        landmarks = result.face_landmarks[0]

        sol_ear = ear_hesapla(landmarks, SOL_GOZ, w, h)
        sag_ear = ear_hesapla(landmarks, SAG_GOZ, w, h)
        ear = (sol_ear + sag_ear) / 2.0
        gaze = gaze_hesapla(landmarks, w, h)
        one_egim, yana_egim = bas_egimi_hesapla(landmarks, w, h)

        if not kalibrasyon_tamam:
            if kal_baslangic is None:
                kal_baslangic = time.time()

            gecen = time.time() - kal_baslangic
            kalan = int(KALIBRASYON_SURE - gecen) + 1

            kal_ear_listesi.append(ear)
            kal_gaze_listesi.append(gaze)
            kal_one_listesi.append(one_egim)
            kal_yana_listesi.append(yana_egim)

            cv2.putText(frame, "KALIBRASYON", (w//2 - 120, h//2 - 60),
                        cv2.FONT_HERSHEY_SIMPLEX, 1.2, (0, 255, 255), 3)
            cv2.putText(frame, "Duz oturun, ekrana bakin", (w//2 - 180, h//2),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.8, (255, 255, 255), 2)
            cv2.putText(frame, f"{kalan} saniye", (w//2 - 60, h//2 + 50),
                        cv2.FONT_HERSHEY_SIMPLEX, 1.0, (0, 255, 0), 2)

            if gecen >= KALIBRASYON_SURE:
                ref_ear = np.mean(kal_ear_listesi)
                ref_gaze = np.mean(kal_gaze_listesi)
                ref_one = np.mean(kal_one_listesi)
                ref_yana = np.mean(kal_yana_listesi)
                kalibrasyon_tamam = True
                print(f"Kalibrasyon tamam! EAR:{ref_ear:.2f} Gaze:{ref_gaze:.2f}")

        else:
            ear_esik = ref_ear * 0.75

            if ear < ear_esik:
                kirpma_sayaci += 1
            else:
                if kirpma_sayaci >= KIRPMA_KARE:
                    toplam_kirpma += 1
                kirpma_sayaci = 0

            gaze_sapma = gaze - ref_gaze
            if gaze_sapma > 0.02:
                gaze_yon = "SOLA BAKIYOR"
                gaze_renk = (0, 165, 255)
            elif gaze_sapma < -0.02:
                gaze_yon = "SAGA BAKIYOR"
                gaze_renk = (0, 165, 255)
            else:
                gaze_yon = "MERKEZE BAKIYOR"
                gaze_renk = (0, 255, 0)

            one_sapma = one_egim - ref_one
            yana_sapma = yana_egim - ref_yana

            if one_sapma > 15:
                bas_durum = "BAS ONE EGILMIS"
                bas_renk = (0, 0, 255)
            elif one_sapma < -8:
                bas_durum = "BAS ARKAYA EGILMIS"
                bas_renk = (0, 0, 255)
            elif yana_sapma > 15:
                bas_durum = "BAS SOLA YATIYOR"
                bas_renk = (0, 165, 255)
            elif yana_sapma < -15:
                bas_durum = "BAS SAGA YATIYOR"
                bas_renk = (0, 165, 255)
            else:
                bas_durum = "BAS DUZGUN"
                bas_renk = (0, 255, 0)

            # Verileri IPC için hazırla
            with veri_kilidi:
                paylasilan_veri.update({
                    "zaman": time.time(),
                    "ear": round(ear, 3),
                    "ear_esik": round(ear_esik, 3),
                    "gaze": round(gaze, 3),
                    "gaze_sapma": round(gaze_sapma, 3),
                    "one_sapma": round(one_sapma, 1),
                    "yana_sapma": round(yana_sapma, 1),
                    "kirpma_sayisi": toplam_kirpma,
                    "yuz_var": True
                })

            # Landmark noktaları
            for i in SOL_GOZ + SAG_GOZ + SOL_IRIS + SAG_IRIS:
                x = int((1 - landmarks[i].x) * w)
                y = int(landmarks[i].y * h)
                cv2.circle(frame, (x, y), 2, (0, 255, 255), -1)

            ear_renk = (0, 0, 255) if ear < ear_esik else (0, 255, 0)
            pipe_durum = "PIPE: BAGLI" if pipe_bagli else "PIPE: BEKLENIYOR"
            pipe_renk = (0, 255, 0) if pipe_bagli else (0, 165, 255)

            cv2.putText(frame, f"EAR: {ear:.2f} (esik:{ear_esik:.2f})", (20, 40),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.7, ear_renk, 2)
            cv2.putText(frame, f"Kirpma: {toplam_kirpma}", (20, 70),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.7, (255, 255, 0), 2)
            cv2.putText(frame, f"Gaze: {gaze:.2f} - {gaze_yon}", (20, 100),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.7, gaze_renk, 2)
            cv2.putText(frame, bas_durum, (20, 130),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.7, bas_renk, 2)
            cv2.putText(frame, pipe_durum, (20, 160),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.6, pipe_renk, 2)

            # EKLENEN KISIM: Gerçek Odak Puanını C#'ın oluşturduğu dosyadan oku
            odak_puani = 100
            try:
                if os.path.exists("aktif_odak.txt"):
                    with open("aktif_odak.txt", "r") as f:
                        odak_puani = int(f.read().strip())
            except:
                pass # Dosya o an yazılıyorsa (kilitliyse) hatayı yoksay, eski puanı göster

            # Puana göre renk belirle (BGR formatında)
            if odak_puani > 70:
                puan_renk = (0, 255, 0) # Yeşil
            elif odak_puani > 40:
                puan_renk = (0, 255, 255) # Sarı
            else:
                puan_renk = (0, 0, 255) # Kırmızı

            cv2.putText(frame, f"ODAK PUANI: {odak_puani} / 100", (20, 200),
                        cv2.FONT_HERSHEY_DUPLEX, 0.9, puan_renk, 2)

    else:
        with veri_kilidi:
            paylasilan_veri.update({"yuz_var": False, "zaman": time.time()})
        cv2.putText(frame, "Yuz Bulunamadi", (20, 40),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.8, (0, 0, 255), 2)

    frame_yaz(frame)

    if not HEADLESS:
        cv2.imshow("FOKUS - Postur Analizi", frame)
        if cv2.waitKey(1) & 0xFF == ord('q'):
            break

cap.release()
if not HEADLESS:
    cv2.destroyAllWindows()
karar_motoru_durdur()
