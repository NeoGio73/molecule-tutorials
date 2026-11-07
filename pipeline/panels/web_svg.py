"""Make web-safe copies of panel SVGs (ChemDraw exports and matplotlib curves) in panels/web/.
- ChemDraw sets Greek in the Symbol font ('b' = beta); browsers off Windows show a plain 'b'. Convert to Unicode in Arial.
- Name every font as Arial with fallbacks, so text matches on Windows, Mac, phones and Linux.
Usage: python3 web_svg.py <panels folder>"""
import os, re, sys
SYM = {'a': 'α', 'b': 'β', 'g': 'γ', 'd': 'δ', 'D': 'Δ', 'q': 'θ', 'p': 'π', 'm': 'μ'}
FALLBACK = "Arial, Helvetica, 'Liberation Sans', sans-serif"

def fix(t):
    t = re.sub(r'(<tspan[^>]*?)font-family="Symbol"([^>]*>)([^<]*)(</tspan>)',
               lambda m: m.group(1) + f'font-family="{FALLBACK}"' + m.group(2) + "".join(SYM.get(c, c) for c in m.group(3)) + m.group(4), t)
    t = t.replace('font-family="Arial"', f'font-family="{FALLBACK}"')
    t = re.sub(r"font-family: '(Liberation Sans|DejaVu Sans|Arial)'", "font-family: " + FALLBACK.replace("'", ""), t)
    return t

src = sys.argv[1]; out = os.path.join(src, "web"); os.makedirs(out, exist_ok=True)
for f in sorted(os.listdir(src)):
    if f.endswith(".svg") and not f.endswith("_preview.svg"):
        t = open(os.path.join(src, f), encoding="utf-8").read()
        n_sym = t.count('font-family="Symbol"')
        open(os.path.join(out, f), "w", encoding="utf-8").write(fix(t))
        print(f"{f}: {n_sym} Symbol runs converted")
