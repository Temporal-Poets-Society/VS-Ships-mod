"""Views of the caravel's draft variants side by side (24 October 2026, chalk note 73: the user to choose between A and B).

    python tools/caravel/variant_views.py [A B]

Captures each variant's viewer page (models/caravel/variants/<v>/caravel.html, written by build_caravel.py with
CARAVEL_OUT) with headless Chrome (SwiftShader), and puts the variants' views side by side in
models/caravel/variants/views/<view>.png, each labelled.
"""
import os
import subprocess
import sys

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
VAR = os.path.join(ROOT, "models", "caravel", "variants")
CHROME = r"C:/Program Files/Google/Chrome/Application/chrome.exe"
HIDE_RIG = "Envelope,Rig_Latina,Rig_Redonda,Sea,Reference,GunsParked,GunPlaces"
HIDE_PLAN = "Envelope,Rig_Redonda,Sea,Reference,GunsParked,GunPlaces"          # (the masts cut where the plan is)
VIEWS = {
    "profile": "profile&hide=Envelope,Rig_Redonda,GunsParked,GunPlaces,Reference",
    "main-deck-plan": f"plan&hide={HIDE_PLAN}&cutz=5600",
    "quarterdeck-plan": f"plan&hide={HIDE_PLAN}&cutz=8600",
    "waist-three-quarter": f"deck&hide={HIDE_PLAN},Grid",
    "stern": f"stern&hide={HIDE_RIG},Grid",
    # (25 October 2026, draft D) the quarterdeck from the head of the stair to starboard, the galley from the waist
    "quarterdeck-from-stair-head": f"custom&eye=-6800,-2000,8700&at=-12500,300,7600&fov=75&hide={HIDE_PLAN},Grid",
    "galley-from-waist": f"custom&eye=-4300,1600,5900&at=-8600,900,4700&fov=70&hide={HIDE_PLAN},Grid",
}
W, H = 1600, 900


def capture(v, view, hash_):
    page = os.path.join(VAR, v, "caravel.html").replace("\\", "/")
    out = os.path.join(VAR, "views", f"{v}-{view}.png")
    url = "file:///" + page.replace(" ", "%20") + "#" + hash_
    subprocess.run([CHROME, "--headless=new", "--disable-gpu", "--use-angle=swiftshader", "--enable-unsafe-swiftshader",
                    f"--window-size={W},{H}", "--timeout=25000", "--hide-scrollbars", f"--screenshot={out}", url],
                   check=False, capture_output=True, timeout=180)
    return out


def main():
    vs = sys.argv[1:] or ["A", "B"]
    os.makedirs(os.path.join(VAR, "views"), exist_ok=True)
    try:
        font = ImageFont.truetype("arial.ttf", 34)
    except OSError:
        font = ImageFont.load_default()
    for view, hash_ in VIEWS.items():
        shots = [Image.open(capture(v, view, hash_)).convert("RGB") for v in vs]
        sheet = Image.new("RGB", (W, H * len(shots) + 10 * (len(shots) - 1)), "white")
        for k, (v, im) in enumerate(zip(vs, shots)):
            sheet.paste(im, (0, k * (H + 10)))
            dr = ImageDraw.Draw(sheet)
            dr.rectangle((1430, k * (H + 10) + 10, 1590, k * (H + 10) + 70), fill="white", outline="black")
            dr.text((1445, k * (H + 10) + 18), f"draft {v}", fill="black", font=font)
        out = os.path.join(VAR, "views", f"{view}.png")
        sheet.save(out)
        print("wrote", out)


if __name__ == "__main__":
    main()
