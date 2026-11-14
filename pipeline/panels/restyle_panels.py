"""Restyle the exported panel SVGs for the tutorials' panel card (decided Nov 8, 2026, "option D"):
- heavier lines, so the drawings stay black when the card shrinks them on screen;
- the drawing's own title is removed (the card's title strip carries it);
- O and N in atom labels take the 3D model's element colors (O red, N blue); C, H, bonds and prose stay black;
- the leaving half of the substrate keeps Lehninger's blue letters (Fig. 6-27); O and N there stay blue too, so the half
  reads as one group; curved arrows stay blue and the moving proton stays magenta. (An optional pale screen behind the
  leaving half, with black letters, is built in; see SCREEN_PANELS.)
Only colors and line weights change: atoms, bonds and positions are exactly as exported from ChemDraw.

Usage: python3 restyle_panels.py <out folder> <svg> [<svg> ...]
Writes <name>.svg (restyled) and prints the title each panel's card should show (see TITLES)."""
import os, re, sys
from cdxml import text_width

BLACK = "rgb(0, 0, 0)"
FUNC_BLUE = "rgb(24, 139, 216)"          # leaving half and curved arrows in the exports
O_RED, N_BLUE = "rgb(214, 38, 38)", "rgb(36, 70, 225)"
SCREEN, SCREEN_EDGE = "rgb(214, 232, 250)", "rgb(120, 175, 230)"
# Nov 8: John kept Lehninger's blue letters for the leaving half (Fig. 6-27), so no panel gets the screen; to try it
# again, list the substrate panels here ("ch_p03_es_complex", "ch_p04_intermediate_acylation", "ch_p05_acyl_enzyme").
SCREEN_PANELS = set()
SMALL_TEXT_SCALE = {"hb_p02_heme_side": 1.3}   # captions of 6-9 pt are unreadable at card size in this wide panel
STROKES = {"0.6": "1.3", "0.48": "1.05", "2": "3.0"}
# ChemDraw exports some bonds as filled outlines instead of stroked lines: bonds that meet at a ring vertex or a chain
# vertex, mitered so the joins are clean. They are 0.6 wide, like the stroked bonds; an outline of 0.7 in their own color
# brings them to 1.3 like the rest (Nov 13: before this, ring and chain bonds stayed at 0.6 and looked thin beside the
# stroked bonds at 1.3). Only thin, long, straight-sided shapes count as bonds; arrowheads and dots are left alone.
BOND_OUTLINE = 0.7
TEXT_WEIGHT = 0.32   # an outline in the text's own color, so 10-pt Arial stays dark when the card is small
# (Unity averages texture pixels in linear light when it shrinks them, which turns thin black lines gray)
LABEL = re.compile(r"^(?:Fe|[CHNOS]|[0-9]|[+−-])+$")   # a formula-style atom label (NH, OH, COO, NH3), not prose

# card title strip: (kicker, title) per panel; the second title is the drawn title removed from the drawing
TITLES = {
    "ch_p01_free_enzyme": ("Mechanism · Lehninger Fig. 6-27", "Free enzyme", "Free enzyme"),
    "ch_p02_hydrophobic_pocket": ("Substrate specificity", "The hydrophobic pocket", "The hydrophobic pocket"),
    "ch_p03_es_complex": ("Mechanism · Lehninger Fig. 6-27", "ES complex", "ES complex"),
    "ch_p04_intermediate_acylation": ("Mechanism · Lehninger Fig. 6-27", "Short-lived intermediate (acylation)", "Short-lived intermediate (acylation)"),
    "ch_p05_acyl_enzyme": ("Mechanism · Lehninger Fig. 6-27", "Acyl-enzyme intermediate", "Acyl-enzyme intermediate"),
    "ch_p06_acyl_enzyme_water": ("Mechanism · Lehninger Fig. 6-27", "Acyl-enzyme intermediate", "Acyl-enzyme intermediate"),
    "ch_p07_intermediate_deacylation": ("Mechanism · Lehninger Fig. 6-27", "Short-lived intermediate (deacylation)", "Short-lived intermediate (deacylation)"),
    "ch_p07b_enzyme_product2": ("Mechanism · Lehninger Fig. 6-27", "Enzyme–product 2 complex", "Enzyme–product 2 complex"),
    "ch_p09_activation": ("Zymogen activation · Lehninger Fig. 6-42", "Activation of chymotrypsinogen", "Activation of chymotrypsinogen"),
    "hb_p01_heme_top": ("The heme", "Heme b (Fe-protoporphyrin IX)", "Heme b (Fe-protoporphyrin IX)"),
    "hb_p02_heme_side": ("Oxygen binding", "The iron moves into the heme plane", None),
    "hb_p04_bohr_pair": ("The Bohr effect", "His<sup>146</sup>β–Asp<sup>94</sup>β ion pair", None),
}


def bond_width(d):
    """Width of a filled bond outline (straight segments only), or None if the shape is not a thin bar."""
    if re.search(r"[^MLZml0-9.\s,-]", d): return None                 # curves: dots, arcs, arrow shafts
    pts = [tuple(map(float, p)) for p in re.findall(r"(-?[0-9.]+)[ ,]+(-?[0-9.]+)", d)]
    if len(pts) < 4: return None
    area = abs(sum(pts[i][0] * pts[i - 1][1] - pts[i - 1][0] * pts[i][1] for i in range(len(pts)))) / 2
    length = max(((a[0] - b[0]) ** 2 + (a[1] - b[1]) ** 2) ** 0.5 for a in pts for b in pts)
    if length < 4: return None
    w = area / length
    return w if w < 1.0 and length / max(w, 1e-6) > 6 else None


def thicken_bond_outline(m):
    el = m.group(0)
    if attr(el, "stroke-width") != "0" or attr(el, "fill") in (None, "none"): return el
    w = bond_width(attr(el, "d", ""))
    if w is None or not 0.45 < w < 0.75: return el
    el = re.sub(r'(?<=\s)stroke="[^"]*"', f'stroke="{attr(el, "fill")}"', el)
    return el.replace('stroke-width="0"', f'stroke-width="{BOND_OUTLINE}"')


def tspans(block):
    return re.findall(r"<tspan([^>]*)>([^<]*)</tspan>", block)


def attr(a, name, default=None):
    m = re.search(r'(?:^|\s)' + name + r'="([^"]*)"', a)
    return m.group(1) if m else default


def color_letters(content):
    """Wrap O and N in element-color tspans."""
    out = []
    for ch in content:
        if ch == "O": out.append(f'<tspan fill="{O_RED}">O</tspan>')
        elif ch == "N": out.append(f'<tspan fill="{N_BLUE}">N</tspan>')
        else: out.append(ch)
    return "".join(out)


def restyle(svg, drawn_title, screen=True, small_scale=1.0):
    boxes = []                                            # leaving-half boxes for the screen (x0, y0, x1, y1)

    def text_block(m):
        block = m.group(0)
        sp = tspans(block)
        joined = "".join(s for _, s in sp)
        if drawn_title and joined == drawn_title:
            return ""
        is_label = bool(LABEL.match(joined)) and ("O" in joined or "N" in joined)
        # leaving-half text: record its box, then treat it as black
        x = y = None
        for a, s in sp:
            size = float(attr(a, "font-size", "10"))
            if attr(a, "x") is not None: x = float(attr(a, "x"))
            if attr(a, "y") is not None: y = float(attr(a, "y"))
            dy = float(attr(a, "dy", "0"))
            w = text_width(s, size, bold="bold" in block)
            if screen and attr(a, "fill") == FUNC_BLUE and x is not None and y is not None:
                yb = y + dy
                boxes.append((x, yb - 0.74 * size, x + w, yb + 0.24 * size))
            if x is not None: x += w
            if y is not None: y += dy

        ys = [float(attr(a, "y")) for a, _ in sp if attr(a, "y") is not None]
        small = small_scale != 1.0 and sp and float(attr(sp[0][0], "font-size", "10")) <= 9

        def one(mt):
            a, s = mt.group(1), mt.group(2)
            if screen: a = a.replace(f'fill="{FUNC_BLUE}"', f'fill="{BLACK}"')
            if small:                                     # bigger caption: scale the size and the line spacing
                a = re.sub(r'font-size="([0-9.]+)"', lambda f: f'font-size="{float(f.group(1)) * small_scale:.2f}"', a)
                if attr(a, "y") is not None and ys:
                    a = re.sub(r'(?<=\s)y="([0-9.]+)"', lambda f: f'y="{ys[0] + (float(f.group(1)) - ys[0]) * small_scale:.2f}"', a)
                if attr(a, "dy") is not None:
                    a = re.sub(r'dy="([-0-9.]+)"', lambda f: f'dy="{float(f.group(1)) * small_scale:.2f}"', a)
            if is_label and f'fill="{BLACK}"' in a:
                s = color_letters(s)
            if TEXT_WEIGHT and attr(a, "fill") is not None and 'stroke=' not in a:
                a += f' stroke="{attr(a, "fill")}" stroke-width="{TEXT_WEIGHT}" stroke-linejoin="round" paint-order="stroke"'
                s = re.sub(r'<tspan fill="([^"]+)">', lambda f: f'<tspan fill="{f.group(1)}" stroke="{f.group(1)}">', s)
            return f"<tspan{a}>{s}</tspan>"
        return re.sub(r"<tspan([^>]*)>([^<]*)</tspan>", one, block)

    svg = re.sub(r"<metadata>.*?</metadata>", "", svg, flags=re.S)
    extra_right = 24 if small_scale != 1.0 else 0       # room for captions that grew to the right
    # widen the frame a little: heavier lines and the screen must not be clipped at ChemDraw's tight viewBox
    svg = re.sub(r'viewBox="([^"]+)"', lambda m: 'viewBox="%.2f %.2f %.2f %.2f"' % (
        lambda v: (v[0] - 4, v[1] - 4, v[2] + 8 + extra_right, v[3] + 8))([float(x) for x in m.group(1).split()]), svg, count=1)
    svg = re.sub(r"<text[^>]*>.*?</text>", text_block, svg, flags=re.S)

    def line(m):
        el = m.group(0)
        if screen and f'stroke="{FUNC_BLUE}"' in el:
            x1, y1, x2, y2 = (float(attr(el, k)) for k in ("x1", "y1", "x2", "y2"))
            boxes.append((min(x1, x2), min(y1, y2), max(x1, x2), max(y1, y2)))
            el = el.replace(f'stroke="{FUNC_BLUE}"', f'stroke="{BLACK}"')
        return el
    svg = re.sub(r"<line[^>]*/>", line, svg)
    svg = re.sub(r'stroke-width="([0-9.]+)"', lambda m: f'stroke-width="{STROKES.get(m.group(1), m.group(1))}"', svg)
    svg = re.sub(r"<path[^>]*/>", thicken_bond_outline, svg)

    if boxes:                                             # one smooth stepped shape: a rectangle per text row
        pad, padx, edge = 2.4, 3.4, 0.55
        rows = []                                         # text and horizontal bonds, grouped by height on the page
        for b in sorted((b for b in boxes if (b[3] - b[1]) <= (b[2] - b[0]) + 3), key=lambda b: (b[1] + b[3]) / 2):
            yc = (b[1] + b[3]) / 2
            if rows and abs(yc - rows[-1]["yc"]) < 6:
                r = rows[-1]["box"]; rows[-1]["box"] = (min(r[0], b[0]), min(r[1], b[1]), max(r[2], b[2]), max(r[3], b[3]))
            else:
                rows.append({"yc": yc, "box": b})
        rows = [r["box"] for r in rows]
        for i in range(1, len(rows)):                     # close the gap between neighbouring rows
            if rows[i][1] - rows[i - 1][3] < 4 * pad:
                mid = (rows[i][1] + rows[i - 1][3]) / 2
                rows[i - 1] = rows[i - 1][:3] + (mid,); rows[i] = (rows[i][0], mid) + rows[i][2:]
        rects = [(x0 - padx, y0 - pad, x1 + padx, y1 + pad) for x0, y0, x1, y1 in rows]
        under = "".join(f'<rect x="{a - edge:.2f}" y="{b - edge:.2f}" width="{c - a + 2 * edge:.2f}" height="{d - b + 2 * edge:.2f}" rx="2" fill="{SCREEN_EDGE}"/>' for a, b, c, d in rects)
        over = "".join(f'<rect x="{a:.2f}" y="{b:.2f}" width="{c - a:.2f}" height="{d - b:.2f}" rx="1.6" fill="{SCREEN}"/>' for a, b, c, d in rects)
        i = min(svg.find(tag) for tag in ("<text", "<line", "<path") if svg.find(tag) >= 0)
        svg = svg[:i] + f'<g id="leaving-half-screen">{under}{over}</g>' + svg[i:]
    return svg, len(boxes)


if __name__ == "__main__":
    out = sys.argv[1]; os.makedirs(out, exist_ok=True)
    for path in sys.argv[2:]:
        name = os.path.splitext(os.path.basename(path))[0]
        kicker, title, drawn = TITLES.get(name, ("", name, None))
        svg, n = restyle(open(path, encoding="utf-8").read(), drawn, screen=name in SCREEN_PANELS,
                         small_scale=SMALL_TEXT_SCALE.get(name, 1.0))
        open(os.path.join(out, name + ".svg"), "w", encoding="utf-8").write(svg)
        print(f"{name}: screen boxes {n}; card: [{kicker}] {title}")
