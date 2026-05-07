import importlib
import importlib.util
import argparse
import shutil
import sys


GEREKLI_MODULLER = [
    ("cv2", "opencv-python"),
    ("numpy", "numpy"),
    ("mediapipe", "mediapipe"),
    ("win32pipe", "pywin32"),
    ("win32file", "pywin32"),
]


def main():
    parser = argparse.ArgumentParser(description="FOKUS Python bagimlilik kontrolu")
    parser.add_argument("--fast", action="store_true", help="Modulleri import etmeden paket varligini kontrol eder.")
    args = parser.parse_args()

    eksikler = []

    if shutil.which("python") is None:
        print("Python PATH uzerinde bulunamadi.")
        return 1

    for modul, paket in GEREKLI_MODULLER:
        try:
            if args.fast:
                if importlib.util.find_spec(modul) is None:
                    raise ImportError("modul bulunamadi")
            else:
                importlib.import_module(modul)
        except Exception as exc:
            eksikler.append((modul, paket, str(exc)))

    if eksikler:
        print("Eksik veya yuklenemeyen Python bagimliliklari:")
        for modul, paket, hata in eksikler:
            print(f"- {modul} ({paket}): {hata}")
        print()
        print("Kurulum icin:")
        print("python -m pip install -r requirements.txt")
        return 1

    print("Python bagimliliklari hazir.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
