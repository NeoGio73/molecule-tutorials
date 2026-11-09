"""Lehninger Fig. 5-9 ion pairs as a schematic, with T and R distances.
Solid line = drawn in the 3D scene (breaks T -> R). Dashed grey = named in Lehninger, not an ion pair in 2HHB (not drawn).
Since Nov 8, 2026 the distances are the closest N-O distance between the two charged groups in each state, from
ion_pair_distances.py (the convention of the paper's Table 2); without that file they fall back to hemoglobin.json,
which follows the atoms that touch in T into R.
Usage: python3 ion_network.py hemoglobin.json out.svg [ion_pairs_closest.json]"""
import json, sys, re
from html import escape

FONT = "Arial, Helvetica, 'Liberation Sans', sans-serif"
CHAIN = {"alpha1": "#ED8C33", "beta1": "#408CE6", "alpha2": "#F2C759", "beta2": "#73CCD9"}   # HbStyle.ChainColor

ROWS = [  # (json name, left node, right node, right side of interface?)
    ("His146β–Asp94β (intrachain)",
     ("beta1", "β1 His146 (HC3)", "imidazolium side chain"), ("beta1", "β1 Asp94 (FG1)", "carboxylate"), False),
    ("His146β carboxylate–Lys40α",
     ("beta1", "β1 His146 (HC3)", "C-terminal carboxylate"), ("alpha2", "α2 Lys40 (C5)", "ε-amino group"), True),
    ("Arg141α guanidinium–Asp126α′",
     ("alpha1", "α1 Arg141 (HC3)", "guanidinium side chain"), ("alpha2", "α2 Asp126 (H9)", "carboxylate"), True),
    ("Arg141α carboxylate–Val1α′ amine",
     ("alpha1", "α1 Arg141 (HC3)", "C-terminal carboxylate"), ("alpha2", "α2 Val1 (NA1)", "N-terminal amino group"), True),
]

AA3 = "Ala|Arg|Asn|Asp|Cys|Gln|Glu|Gly|His|Ile|Leu|Lys|Met|Phe|Pro|Ser|Thr|Trp|Tyr|Val"
def sup(text):
    """Escape text and set residue numbers (His146) as superscripts, Lehninger style."""
    return re.sub(r"(?<=(?:%s))(\d+)" % AA3, r'<tspan baseline-shift="super" font-size="68%">\1</tspan>', escape(text))

def main(jpath, out, closest_path=None):
    d = json.load(open(jpath))
    dist = {p["name"]: (p["distT"], p["distR"], p.get("hidden", False)) for p in d["ionPairs"] if p["side"] == "primary"}
    if closest_path:
        cl = json.load(open(closest_path, encoding="utf-8"))
        dist = {n: ((cl[n]["distT"], cl[n]["distR"], h) if n in cl else v) for n, v in dist.items() for h in [v[2]]}
    W, H = 1000, 520
    x_if = 478                                  # the α1β1 / α2β2 interface
    L0, L1 = 24, 228                            # left node box
    B0, B1 = 262, 446                           # intrachain partner box (left of interface)
    R0, R1 = 510, 714                           # partner box across the interface
    D0 = 748                                    # distance column
    out_ = []
    a = out_.append
    a(f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W} {H}" width="{W}" height="{H}" font-family="{FONT}">')
    a(f'<rect x="0" y="0" width="{W}" height="{H}" fill="#ffffff"/>')
    # The title ("T-state ion pairs (Lehninger Fig. 5-9), one interface") is on the tutorial's card strip since Nov 8.
    a(f'<text x="{(L0 + B1) / 2}" y="70" font-size="17" text-anchor="middle" fill="#444">α1β1 dimer</text>')
    a(f'<text x="{(R0 + R1) / 2}" y="70" font-size="17" text-anchor="middle" fill="#444">α2β2 dimer</text>')
    a(f'<text x="{D0}" y="70" font-size="17" fill="#444">distance, T → R</text>')
    a(f'<line x1="{x_if}" y1="82" x2="{x_if}" y2="{H - 78}" stroke="#888" stroke-width="1.8" stroke-dasharray="6,5"/>')
    a(f'<text x="{x_if}" y="{H - 62}" font-size="14" text-anchor="middle" fill="#666">interface</text>')

    def node(x0, x1, yc, chain, title, sub, grey=False):
        h = 54; y0 = yc - h / 2
        stroke = "#9a9a9a" if grey else "#333"
        tcol = "#777" if grey else "#111"
        a(f'<rect x="{x0}" y="{y0}" width="{x1 - x0}" height="{h}" rx="6" fill="#fff" stroke="{stroke}" stroke-width="1.8"/>')
        a(f'<rect x="{x0}" y="{y0}" width="9" height="{h}" rx="3" fill="{CHAIN[chain]}" opacity="{0.55 if grey else 1}"/>')
        a(f'<text x="{x0 + 18}" y="{yc - 4}" font-size="18" fill="{tcol}">{sup(title)}</text>')
        a(f'<text x="{x0 + 18}" y="{yc + 17}" font-size="14" fill="{"#888" if grey else "#555"}">{escape(sub)}</text>')

    y = 118
    for name, left, right, across in ROWS:
        dT, dR, hidden = dist[name]
        breaks = dR > dT + 2.0
        grey = hidden and not breaks
        node(L0, L1, y, *left, grey=False)
        if across:
            node(R0, R1, y, *right, grey=grey); xe = R0
        else:
            node(B0, B1, y, *right); xe = B0
        style = 'stroke="#9a9a9a" stroke-width="2.6" stroke-dasharray="8,6"' if grey else 'stroke="#111" stroke-width="3.4"'
        a(f'<line x1="{L1}" y1="{y}" x2="{xe}" y2="{y}" {style}/>')
        # the hidden pair is not an ion pair in T; its R distance is not shown (the α C-termini are poorly
        # ordered in 1HHO, B > 80 Å², and their modeled contacts are closer than any real N–O contact)
        shown = f"{dT:.1f} Å in T" if hidden else f"{dT:.1f} Å → {dR:.1f} Å"
        a(f'<text x="{D0}" y="{y + 1}" font-size="20" fill="{"#777" if grey else "#111"}">{shown}</text>')
        note = "breaks" if breaks else "not an ion pair in 2HHB"
        a(f'<text x="{D0}" y="{y + 21}" font-size="14" fill="{"#888" if grey else "#555"}">{escape(note)}</text>')
        y += 86
    a(f'<text x="{L0}" y="{H - 30}" font-size="13.5" fill="#555">Closest N–O distance between the charged groups, in 2HHB (T) and 1HHO (R). The symmetric interface shows the same pattern.</text>')
    a(f'<text x="{L0}" y="{H - 12}" font-size="13.5" fill="#555">Solid: drawn in the 3D scene and broken in R. Dashed: in Lehninger’s figure, but not an ion pair in the crystal structure.</text>')
    a('</svg>')
    open(out, "w", encoding="utf-8").write("\n".join(out_))
    for name, *_ in ROWS:
        print(name, dist[name])

if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2], sys.argv[3] if len(sys.argv) > 3 else None)
