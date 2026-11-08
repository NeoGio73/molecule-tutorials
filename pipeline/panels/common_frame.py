"""Put a set of ChemDraw SVG exports on one shared frame, so frames shown one after another don't jump.
ChemDraw crops every SVG to its own drawing, but all the mechanism frames are drawn in the same page coordinates
(make_chymo.py), so giving them all the union of their viewBoxes lines them up exactly.
Usage: python3 common_frame.py file1.svg file2.svg ...   (rewrites the files in place; run it on the web copies)"""
import re, sys

PAD = 2.0
files = sys.argv[1:]
boxes = {}
for f in files:
    t = open(f, encoding="utf-8").read()
    x, y, w, h = map(float, re.search(r'viewBox="([^"]+)"', t).group(1).split())
    boxes[f] = (x, y, x + w, y + h)
x0 = min(b[0] for b in boxes.values()) - PAD; y0 = min(b[1] for b in boxes.values()) - PAD
x1 = max(b[2] for b in boxes.values()) + PAD; y1 = max(b[3] for b in boxes.values()) + PAD
vb = f"{x0:.2f} {y0:.2f} {x1 - x0:.2f} {y1 - y0:.2f}"
for f in files:
    t = open(f, encoding="utf-8").read()
    t = re.sub(r'viewBox="[^"]+"', f'viewBox="{vb}"', t, count=1)
    # ChemDraw's white background starts at the old crop corner; move it to the new one
    t = re.sub(r'<rect x="[^"]+" y="[^"]+" width="100%" height="100%"', f'<rect x="{x0:.2f}" y="{y0:.2f}" width="100%" height="100%"', t, count=1)
    open(f, "w", encoding="utf-8").write(t)
print("shared viewBox", vb, "for", len(files), "files")
