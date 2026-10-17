"""Bundle a ship's mesh data into the shared ship viewer (tools/shipview/viewer.html): one self-contained page that opens
anywhere, including the desktop app's side panel (no local files to fetch).

    python tools/shipview/bundle.py <ship>-mesh.js <out>.html

The data file sets window.SHIP (or window.CARAVEL); its contract is at the top of viewer.html.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
MARK = "<!-- SHIP-DATA -->"


def bundle(data_js, out):
    page = open(os.path.join(HERE, "viewer.html"), encoding="utf-8").read()
    if MARK not in page:
        raise SystemExit(f"{MARK} not found in viewer.html")
    data = open(data_js, encoding="utf-8").read()
    with open(out, "w", encoding="utf-8") as f:
        f.write(page.replace(MARK, "<script>\n" + data + "\n</script>"))
    print("wrote", os.path.normpath(out), f"{os.path.getsize(out) / 1e6:.1f} MB")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)
    bundle(sys.argv[1], sys.argv[2])
