"""Minimal CDXML writer for ChemDraw panels.
Coordinates are in points, y down, ACS 1996 bond length 14.4 pt.
"""
import math
from rdkit import Chem
from rdkit.Chem import rdDepictor

BL = 14.4          # ACS Document 1996 bond length (pt)
FONT_ARIAL = 3
FONT_TIMES = 4
FONT_SYMBOL = 5
GREEK = {'α': 'a', 'β': 'b', 'γ': 'g', 'δ': 'd', 'Δ': 'D', 'θ': 'q', 'π': 'p'}
SUB, SUP = 32, 64
# Lehninger's mechanism-figure colors; CDX color index = position in the colortable + 2
COLOR = {"black": 3, "red": 10, "magenta": 11, "blue": 12, "grey": 13}      # ChemDraw face bits: 1 bold, 2 italic, 32 subscript, 64 superscript

PT = {1: "H", 6: "C", 7: "N", 8: "O", 15: "P", 16: "S", 26: "Fe"}


class Doc:
    def __init__(self, circled=True):
        self.items = []
        self._id = 100
        self.circled = circled          # formal charges drawn as ChemDraw circle-plus / circle-minus symbols
        self._pending = []

    def _flush_symbols(self):
        for (x, y, c) in self._pending:
            self.charge_symbol(x, y, c)
        self._pending = []

    def nid(self):
        self._id += 1
        return self._id

    # ---------- molecules
    def add_mol(self, mol, ox=0.0, oy=0.0, rot_deg=0.0, extra_atoms=(), extra_bonds=(), charges=None, hcount=None, unit=1.5, nowarn=(), mirror=False):
        """mol: RDKit mol with 2D conformer (RDKit units ~ 1.5 per bond). Places it so that the
        centroid is at (ox, oy). extra_atoms: list of dicts {sym, x, y (in BL units relative to centroid)}.
        extra_bonds: list of (i, j, order) with indices into mol atoms followed by extra atoms.
        charges: {atom_idx: charge}, hcount: {atom_idx: n}."""
        conf = mol.GetConformer()
        pts = [(conf.GetAtomPosition(i).x, conf.GetAtomPosition(i).y) for i in range(mol.GetNumAtoms())]
        if unit is None:
            bl = [math.dist(pts[b.GetBeginAtomIdx()], pts[b.GetEndAtomIdx()]) for b in mol.GetBonds()]
            unit = sum(bl) / len(bl)
        # scale: RDKit bond ~1.5 -> BL
        cx = sum(p[0] for p in pts) / len(pts)
        cy = sum(p[1] for p in pts) / len(pts)
        th = math.radians(rot_deg)
        out = []
        for (x, y) in pts:
            x, y = x - cx, y - cy
            if mirror: y = -y
            xr = x * math.cos(th) - y * math.sin(th)
            yr = x * math.sin(th) + y * math.cos(th)
            out.append((ox + xr / unit * BL, oy - yr / unit * BL))   # flip y (RDKit y up, CDX y down)
        for ea in extra_atoms:
            out.append((ox + ea["x"] * BL, oy + ea["y"] * BL))
        ids = [self.nid() for _ in out]
        nodes = []
        for i, (x, y) in enumerate(out):
            if i < mol.GetNumAtoms():
                a = mol.GetAtomWithIdx(i)
                z = a.GetAtomicNum()
                chg = a.GetFormalCharge()
                nh = a.GetTotalNumHs()
            else:
                ea = extra_atoms[i - mol.GetNumAtoms()]
                z = Chem.GetPeriodicTable().GetAtomicNumber(ea["sym"])
                chg = ea.get("charge", 0)
                nh = ea.get("h", 0)
            if charges and i in charges: chg = charges[i]
            if hcount and i in hcount: nh = hcount[i]
            attrs = f'id="{ids[i]}" p="{x:.2f} {y:.2f}"'
            if z != 6:
                attrs += f' Element="{z}"'
                attrs += f' NumHydrogens="{nh}"'
            if chg:
                if self.circled:
                    attrs += ' IgnoreWarnings="yes"'
                    nbs = [out[n.GetIdx()] for n in mol.GetAtomWithIdx(i).GetNeighbors()] if i < mol.GetNumAtoms() else []
                    side = 1.0 if (not nbs or x - nbs[0][0] >= 0) else -1.0
                    w = 4.0 + 8.0 * nh
                    self._pending.append((x + side * (w + 3.0) - 1.25, y - 4.75, chg))
                else:
                    attrs += f' Charge="{chg}"'
            if i in nowarn:
                attrs += ' IgnoreWarnings="yes"'
            nodes.append(f'<n {attrs}/>')
        bonds = []
        for b in mol.GetBonds():
            o = {Chem.BondType.SINGLE: "1", Chem.BondType.DOUBLE: "2", Chem.BondType.TRIPLE: "3", Chem.BondType.AROMATIC: "1.5"}[b.GetBondType()]
            bonds.append(f'<b id="{self.nid()}" B="{ids[b.GetBeginAtomIdx()]}" E="{ids[b.GetEndAtomIdx()]}" Order="{o}"/>')
        for (i, j, o) in extra_bonds:
            bonds.append(f'<b id="{self.nid()}" B="{ids[i]}" E="{ids[j]}" Order="{o}"/>')
        self.items.append(f'<fragment id="{self.nid()}">\n' + "\n".join(nodes + bonds) + "\n</fragment>")
        self._flush_symbols()
        return out, ids

    def add_fragment_raw(self, atoms, bonds):
        """atoms: list of dicts {sym, x, y (pt), charge?, h?}; bonds: (i, j, order)."""
        ids = [self.nid() for _ in atoms]
        nodes = []
        for i, a in enumerate(atoms):
            if a.get("label"):      # text label node (abbreviation); ChemDraw draws bonds to the label itself
                nodes.append(f'<n id="{ids[i]}" p="{a["x"]:.2f} {a["y"]:.2f}" NodeType="Unspecified" IgnoreWarnings="yes">'
                             f'<t p="{a["x"]:.2f} {a["y"]:.2f}"{label_align(a)}>{label_xml(a["label"], a.get("size", 10), a.get("color"))}</t></n>')
                continue
            z = Chem.GetPeriodicTable().GetAtomicNumber(a["sym"])
            attrs = f'id="{ids[i]}" p="{a["x"]:.2f} {a["y"]:.2f}"'
            if z != 6:
                attrs += f' Element="{z}" NumHydrogens="{a.get("h", 0)}"'
            if a.get("charge"):
                if self.circled:
                    attrs += ' IgnoreWarnings="yes"'
                    nb = [atoms[j] if k == i else atoms[k] for (k, j, *_) in bonds if i in (k, j)]
                    side = 1.0
                    if nb:
                        side = 1.0 if (a["x"] - nb[0]["x"]) >= 0 else -1.0     # charge away from the bonded atom
                    w = 4.0 + 8.0 * a.get("h", 0)
                    if a.get("charge_at"):
                        self._pending.append((a["charge_at"][0], a["charge_at"][1], a["charge"]))
                    else:
                        self._pending.append((a["x"] + side * (w + 3.0) - 1.25, a["y"] - 4.75, a["charge"]))
                else:
                    attrs += f' Charge="{a["charge"]}"'
            if a.get("nowarn") and 'IgnoreWarnings' not in attrs:
                attrs += ' IgnoreWarnings="yes"'           # e.g. an O whose other bond is drawn as a plain line
            if a.get("labelsize"):
                attrs += f' LabelSize="{a["labelsize"]}"'
            if a.get("color"):
                attrs += f' color="{COLOR[a["color"]]}"'
            nodes.append(f'<n {attrs}/>')
        bl = []
        for (i, j, o, *rest) in bonds:
            d = rest[0] if len(rest) > 0 else None
            col = rest[1] if len(rest) > 1 else None
            if col is None and atoms[i].get("color") and atoms[i].get("color") == atoms[j].get("color"):
                col = atoms[i]["color"]                     # a bond inside one colored half takes that color
            extra = (f' Display="{d}"' if d else "") + (f' color="{COLOR[col]}"' if col else "")
            bl.append(f'<b id="{self.nid()}" B="{ids[i]}" E="{ids[j]}" Order="{o}"{extra}/>')
        self.items.append(f'<fragment id="{self.nid()}">\n' + "\n".join(nodes + bl) + "\n</fragment>")
        self._flush_symbols()
        return ids

    # ---------- text, lines, arrows
    def text(self, x, y, runs, font=FONT_ARIAL, size=10, align=None, color=None):
        """runs: str or list of (text, face[, color]) with face bits: 1 bold, 2 italic, 32 subscript, 64 superscript."""
        if isinstance(runs, str):
            runs = [(runs, 0)]
        s = "".join(runs_xml(r[0], font, size, r[1], r[2] if len(r) > 2 else color) for r in runs)
        just = f' Justification="{align}"' if align else ""
        self.items.append(f'<t id="{self.nid()}" p="{x:.2f} {y:.2f}"{just}>{s}</t>')

    def line(self, x1, y1, x2, y2, dashed=False, bold=False, color=None):
        lt = "Dashed" if dashed else ("Bold" if bold else "Solid")
        col = f' color="{COLOR[color]}"' if color else ""
        self.items.append(f'<graphic id="{self.nid()}" BoundingBox="{x1:.2f} {y1:.2f} {x2:.2f} {y2:.2f}" '
                          f'GraphicType="Line" LineType="{lt}"{col}/>')

    def charge_symbol(self, x, y, sign):
        """ChemDraw circled charge symbol centred at (x, y). ChemDraw puts a symbol at the FIRST point of its
        BoundingBox, upright and at a fixed size (r = 4.1 pt), whatever the second point says (checked in its SVG export)."""
        st = "CirclePlus" if sign > 0 else "CircleMinus"
        self.items.append(f'<graphic id="{self.nid()}" BoundingBox="{x:.2f} {y:.2f} {x+6.5:.2f} {y+6.5:.2f}" GraphicType="Symbol" SymbolType="{st}"/>')

    def dot(self, x, y, r=0.9):
        """Small filled circle (one electron of a lone pair), written with explicit centre and axis ends."""
        self.items.append(f'<graphic id="{self.nid()}" BoundingBox="{x:.2f} {y:.2f} {x+r:.2f} {y:.2f}" GraphicType="Oval" OvalType="Filled" '
                          f'Center3D="{x:.2f} {y:.2f} 0" MajorAxisEnd3D="{x+r:.2f} {y:.2f} 0" MinorAxisEnd3D="{x:.2f} {y+r:.2f} 0"/>')

    def lone_pair(self, x, y, dx, dy, gap=1.9):
        """Two dots centred at (x, y), side by side across the direction (dx, dy) the pair points."""
        import math
        L = math.hypot(dx, dy); px, py = -dy / L * gap, dx / L * gap
        self.dot(x + px, y + py); self.dot(x - px, y - py)

    def oval(self, x1, y1, x2, y2):
        self.items.append(f'<graphic id="{self.nid()}" BoundingBox="{x1:.2f} {y1:.2f} {x2:.2f} {y2:.2f}" GraphicType="Oval" OvalType="Circle" LineWidth="0.5"/>')

    def arrow(self, x1, y1, x2, y2, head="Full", tail="Unspecified", dashed=False):
        """Straight arrow from tail (x1,y1) to head (x2,y2). head/tail: Full, HalfLeft, HalfRight, Unspecified."""
        lt = ' LineType="Dashed"' if dashed else ""
        self.items.append(f'<arrow id="{self.nid()}" FillType="None" ArrowheadHead="{head}" ArrowheadTail="{tail}" '
                          f'ArrowheadType="Solid" HeadSize="1000" ArrowheadCenterSize="875" ArrowheadWidth="250"{lt} '
                          f'Head3D="{x2:.2f} {y2:.2f} 0" Tail3D="{x1:.2f} {y1:.2f} 0"/>')

    def curved_arrow(self, tail, head, sweep=110.0, side=1, color="red"):
        """Curved electron-pushing arrow from tail to head along a circular arc of `sweep` degrees.
        side = +1 bulges to the left of the tail->head direction (on the page), -1 to the right.
        Written the way ChemDraw stores arcs: Center3D, MajorAxisEnd3D (= the tail), MinorAxisEnd3D
        (a quarter turn toward the head), AngularSize = sweep in degrees."""
        import math
        (tx, ty), (hx, hy) = tail, head
        cx_, cy_ = (tx + hx) / 2, (ty + hy) / 2
        c = math.hypot(hx - tx, hy - ty)
        phi = math.radians(sweep)
        r = c / (2 * math.sin(phi / 2))
        # unit normal to the chord, pointing to the bulge side (page coordinates, y down)
        nx, ny = -(hy - ty) / c, (hx - tx) / c
        if side < 0: nx, ny = -nx, -ny
        d = r * math.cos(phi / 2)
        Cx, Cy = cx_ - nx * d, cy_ - ny * d          # centre sits opposite the bulge
        ux, uy = (tx - Cx) / r, (ty - Cy) / r
        wx, wy = (hx - Cx) / r, (hy - Cy) / r
        vx, vy = -uy, ux                              # quarter turn; flip if it turns away from the head
        if abs(math.cos(phi) * ux + math.sin(phi) * vx - wx) + abs(math.cos(phi) * uy + math.sin(phi) * vy - wy) > 1e-3:
            vx, vy = -vx, -vy
        col = f' color="{COLOR[color]}"' if color else ""
        # ChemDraw draws the arc so that it ENDS at Head3D and starts AngularSize degrees away from it, measured
        # in page coordinates (y down): a positive AngularSize runs counterclockwise on screen, a negative one clockwise.
        # Learned from ch_p03: with side=-1 and a positive angle the tail jumped to the far side of the head.
        ang = sweep if side > 0 else -sweep
        self.items.append(f'<arrow id="{self.nid()}" FillType="None" ArrowheadHead="Full" ArrowheadType="Solid" '
                          f'HeadSize="1000" ArrowheadCenterSize="875" ArrowheadWidth="250" AngularSize="{ang:.1f}"{col} '
                          f'Head3D="{hx:.2f} {hy:.2f} 0" Tail3D="{tx:.2f} {ty:.2f} 0" Center3D="{Cx:.2f} {Cy:.2f} 0" '
                          f'MajorAxisEnd3D="{Cx + r*ux:.2f} {Cy + r*uy:.2f} 0" MinorAxisEnd3D="{Cx + r*vx:.2f} {Cy + r*vy:.2f} 0"/>')

    def equilibrium(self, x1, x2, y, gap=2.5):
        self.arrow(x1, y - gap, x2, y - gap, head="HalfLeft")
        self.arrow(x2, y + gap, x1, y + gap, head="HalfLeft")

    def write(self, path, width=540, height=400):
        body = "\n".join(self.items)
        xml = f'''<?xml version="1.0" encoding="UTF-8" ?>
<!DOCTYPE CDXML SYSTEM "http://www.cambridgesoft.com/xml/cdxml.dtd" >
<CDXML CreationProgram="hb_pipeline cdxml.py" BondLength="{BL:.2f}" BondSpacing="18" LineWidth="0.60" BoldWidth="2" HashSpacing="2.50" MarginWidth="1.60"
 LabelFont="{FONT_ARIAL}" LabelSize="10" LabelFace="96" CaptionFont="{FONT_ARIAL}" CaptionSize="10" CaptionFace="0"
 CaptionJustification="Left" FixInplaceExtent="6 0" AminoAcidTermini="HOH" ShowSequenceTermini="yes" ShowSequenceBonds="yes" ShowSequenceUnlinkedBranches="no" HideImplicitHydrogens="no">
<colortable>
<color r="1" g="1" b="1"/>
<color r="0" g="0" b="0"/>
<color r="1" g="0" b="0"/>
<color r="1" g="1" b="0"/>
<color r="0" g="1" b="0"/>
<color r="0" g="1" b="1"/>
<color r="0" g="0" b="1"/>
<color r="1" g="0" b="1"/>
<color r="0.85" g="0.1" b="0.1"/>
<color r="0.78" g="0.1" b="0.5"/>
<color r="0.1" g="0.55" b="0.85"/>
<color r="0.55" g="0.55" b="0.55"/>
</colortable>
<fonttable>
<font id="{FONT_ARIAL}" charset="iso-8859-1" name="Arial"/>
<font id="{FONT_TIMES}" charset="iso-8859-1" name="Times New Roman"/>
<font id="{FONT_SYMBOL}" charset="Symbol" name="Symbol"/>
</fonttable>
<page id="{self.nid()}" BoundingBox="0 0 {width} {height}" HeaderPosition="36" FooterPosition="36" PageOverlap="0" PrintTrimMarks="yes" HeightPages="1" WidthPages="1">
{body}
</page>
</CDXML>
'''
        with open(path, "w", encoding="utf-8") as f:
            f.write(xml)


def label_align(a):
    """Optional fixed alignment for a label node ("Right" for a label at the left end of a chain), so ChemDraw shows the
    text as written instead of reversing it."""
    al = a.get("align")
    return f' LabelJustification="{al}" LabelAlignment="{al}"' if al else ""


def label_xml(label, size, color=None):
    """A label node's text: a plain string in ChemDraw's formula face, or a list of (text, face[, color]) runs."""
    if isinstance(label, str):
        return runs_xml(label, FONT_ARIAL, size, 96, color)
    return "".join(runs_xml(r[0], FONT_ARIAL, size, r[1], r[2] if len(r) > 2 else color) for r in label)


def runs_xml(t, font, size, face, color=None):
    """Split a run so Greek letters are set in the Symbol font (ChemDraw convention; Latin-1 fonts turn beta into sharp s)."""
    out, buf = [], ""
    c = f' color="{COLOR[color]}"' if color else ""
    for ch in t:
        if ch in GREEK:
            if buf: out.append(f'<s font="{font}" size="{size}" face="{face}"{c}>{esc(buf)}</s>'); buf = ""
            out.append(f'<s font="{FONT_SYMBOL}" size="{size}" face="{face}"{c}>{GREEK[ch]}</s>')
        else:
            buf += ch
    if buf: out.append(f'<s font="{font}" size="{size}" face="{face}"{c}>{esc(buf)}</s>')
    return "".join(out)


def esc(s):
    return s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def mol2d(smiles, coordgen=True):
    m = Chem.MolFromSmiles(smiles)
    rdDepictor.SetPreferCoordGen(coordgen)
    rdDepictor.Compute2DCoords(m)
    return m


# ---------- crude preview (for checking layout before ChemDraw) ----------
import re
def _pcol(root, idx):
    if not idx: return "k"
    cols = root.find("colortable").findall("color")
    i = int(idx) - 2
    if 0 <= i < len(cols): return (float(cols[i].get("r")), float(cols[i].get("g")), float(cols[i].get("b")))
    return "k"

def preview(path, png, scale=3):
    import xml.etree.ElementTree as ET
    import matplotlib; matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    plt.rcParams["font.family"] = "Liberation Sans"       # metric twin of Arial, so text widths match ChemDraw
    root = ET.parse(path).getroot()
    page = root.find("page")
    W, H = [float(v) for v in page.get("BoundingBox").split()[2:]]
    fig, ax = plt.subplots(figsize=(W/72*scale, H/72*scale), dpi=72)
    ax.set_xlim(0, W); ax.set_ylim(H, 0); ax.set_aspect("equal"); ax.axis("off")
    pos_all, nbrs = {}, {}
    for frag in page.iter("fragment"):
        for n in frag.findall("n"): pos_all[n.get("id")] = tuple(float(v) for v in n.get("p").split())
        for b in frag.findall("b"):
            nbrs.setdefault(b.get("B"), []).append(b.get("E")); nbrs.setdefault(b.get("E"), []).append(b.get("B"))
    def _clip(p, q, box):
        """Move p toward q until it leaves box (xmin, ymin, xmax, ymax); p is the box's own node."""
        if box is None: return p
        (x0, y0, x1, y1) = box; dx, dy = q[0] - p[0], q[1] - p[1]; t = 1.0
        for d_, lo, hi, c in ((dx, x0, x1, p[0]), (dy, y0, y1, p[1])):
            if d_ > 1e-9: t = min(t, (hi - c) / d_)
            elif d_ < -1e-9: t = min(t, (lo - c) / d_)
        t = max(0.0, t)
        return (p[0] + t * dx, p[1] + t * dy)
    for frag in page.iter("fragment"):
        pos, box = {}, {}
        for n in frag.findall("n"):
            x, y = [float(v) for v in n.get("p").split()]
            pos[n.get("id")] = (x, y)
            z = int(n.get("Element", 6))
            tl = n.find("t")
            if tl is not None:      # label node, laid out the way ChemDraw does: attachment character centred on the node
                runs = tl.findall("s")
                lab = "".join(st.text or "" for st in runs)
                nb = [pos_all[o] for o in nbrs.get(n.get("id"), []) if o in pos_all]
                col = _pcol(root, runs[0].get("color") if runs else None)
                wid = sum(text_width(st.text or "") * (0.72 if int(st.get("face", 0)) & 96 and int(st.get("face", 0)) != 96 else 1) for st in runs)
                if tl.get("LabelAlignment") != "Left" and nb and all(q[0] > x + 1 for q in nb):   # bond only from the right: ChemDraw reverses the tokens
                    toks = re.findall(r"[A-Z][^A-Z]*", lab) or [lab]
                    xr = x + text_width(toks[0][0]) / 2 + (text_width(toks[0][1:]) * 0.72 if len(toks[0]) > 1 else 0)
                    ax.text(xr, y + 3.5, "".join(reversed(toks)), ha="right", va="baseline", fontsize=10*scale, color=col)
                    box[n.get("id")] = (xr - wid - 1.6, y - 5.2, xr + 1.6, y + 5.2)
                else:
                    xl = x - text_width(lab[0]) / 2
                    ax.text(xl, y + 3.5, lab, ha="left", va="baseline", fontsize=10*scale, color=col)
                    box[n.get("id")] = (xl - 1.6, y - 5.2, xl + wid + 1.6, y + 5.2)
                continue
            if z != 6:
                lab = PT.get(z, str(z)); nh = int(n.get("NumHydrogens", 0)); ch = int(n.get("Charge", 0))
                if nh: lab += "H" + (str(nh) if nh > 1 else "")
                if ch: lab += ("+" if ch > 0 else "–")
                ax.text(x, y, lab, ha="center", va="center", fontsize=10*scale, color=_pcol(root, n.get("color")))
                hw = text_width(lab[0]) / 2 + 1.6
                box[n.get("id")] = (x - hw, y - 5.2, x + hw + (text_width(lab[1:]) if len(lab) > 1 else 0), y + 5.2)
        for b in frag.findall("b"):
            pb_, pe_ = pos[b.get("B")], pos[b.get("E")]
            (x1, y1) = _clip(pb_, pe_, box.get(b.get("B")))
            (x2, y2) = _clip(pe_, pb_, box.get(b.get("E")))
            o = b.get("Order", "1")
            bc = _pcol(root, b.get("color"))
            if o == "2":
                dx, dy = x2-x1, y2-y1; L = (dx*dx+dy*dy)**0.5 or 1; nx, ny = -dy/L*1.3, dx/L*1.3
                for sgn in (1, -1):
                    ax.plot([x1+sgn*nx, x2+sgn*nx], [y1+sgn*ny, y2+sgn*ny], color=bc, lw=0.8*scale, zorder=0)
            else:
                ax.plot([x1, x2], [y1, y2], color=bc, lw=0.8*scale, zorder=0)
            if o == "dative":
                ax.annotate("", xy=(x2, y2), xytext=(x1, y1), arrowprops=dict(arrowstyle="->", lw=0.8*scale))
    for t in page.findall("t"):
        x, y = [float(v) for v in t.get("p").split()]
        sr = t.findall("s"); s = "".join(st.text or "" for st in sr)
        fs = float(sr[0].get("size", 10)) if sr else 10
        ax.text(x, y, s, ha={"Center": "center", "Right": "right"}.get(t.get("Justification"), "left"), va="baseline", fontsize=fs*scale,
                color=_pcol(root, sr[0].get("color") if sr else None), weight="bold" if sr and int(sr[0].get("face", 0)) & 1 else "normal")
    for g in page.findall("graphic"):
        x1, y1, x2, y2 = [float(v) for v in g.get("BoundingBox").split()]
        from matplotlib.patches import Circle
        if g.get("GraphicType") == "Oval" and g.get("Center3D"):
            cx, cy, _ = [float(v) for v in g.get("Center3D").split()]
            mx, my, _ = [float(v) for v in g.get("MajorAxisEnd3D").split()]
            ax.add_patch(Circle((cx, cy), ((mx-cx)**2 + (my-cy)**2) ** 0.5, color="k", lw=0))
            continue
        if g.get("GraphicType") == "Symbol":
            cx, cy, r = x1, y1, 4.1
            ax.add_patch(Circle((cx, cy), r, fill=False, lw=0.6*scale, color="k"))
            ax.plot([cx - r*0.6, cx + r*0.6], [cy, cy], color="k", lw=0.6*scale)
            if g.get("SymbolType") == "CirclePlus": ax.plot([cx, cx], [cy - r*0.6, cy + r*0.6], color="k", lw=0.6*scale)
            continue
        ax.plot([x1, x2], [y1, y2], color=_pcol(root, g.get("color")) if g.get("color") else "k", lw=(2.5 if g.get("LineType") == "Bold" else 0.8)*scale,
                ls="--" if g.get("LineType") == "Dashed" else "-")
    for a in page.findall("arrow"):
        hx, hy, _ = [float(v) for v in a.get("Head3D").split()]
        tx, ty, _ = [float(v) for v in a.get("Tail3D").split()]
        col = _pcol(root, a.get("color"))
        if a.get("Center3D"):
            import numpy as _np
            Cx, Cy, _ = [float(v) for v in a.get("Center3D").split()]
            Mx, My, _ = [float(v) for v in a.get("MajorAxisEnd3D").split()]
            Nx, Ny, _ = [float(v) for v in a.get("MinorAxisEnd3D").split()]
            # ChemDraw's rule (seen in ch_p03): the arc ends at the head and starts AngularSize degrees from it,
            # angles in page coordinates; so the preview draws from head + AngularSize back to the head
            r = ((Mx - Cx) ** 2 + (My - Cy) ** 2) ** 0.5
            th = _np.arctan2(hy - Cy, hx - Cx)
            t = _np.linspace(th + _np.radians(float(a.get("AngularSize"))), th, 60)
            xs, ys = Cx + r * _np.cos(t), Cy + r * _np.sin(t)
            ax.plot(xs[:-3], ys[:-3], color=col, lw=0.8*scale)
            ax.annotate("", xy=(xs[-1], ys[-1]), xytext=(xs[-4], ys[-4]), arrowprops=dict(arrowstyle="-|>", lw=0.8*scale, color=col, mutation_scale=6*scale))
        else:
            ax.annotate("", xy=(hx, hy), xytext=(tx, ty), arrowprops=dict(arrowstyle="->", lw=0.8*scale, color=col))
    fig.savefig(png, bbox_inches="tight", facecolor="white"); plt.close(fig)


# ---------- text width in points, Arial metrics (Liberation Sans is metric-compatible)
def text_width(txt, size=10, bold=False):
    from PIL import ImageFont
    f = ImageFont.truetype("/usr/share/fonts/truetype/liberation/LiberationSans-%s.ttf" % ("Bold" if bold else "Regular"), 100)
    return f.getlength(txt) * size / 100.0
