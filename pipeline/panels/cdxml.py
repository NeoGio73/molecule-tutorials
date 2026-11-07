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
SUB, SUP = 32, 64      # ChemDraw face bits: 1 bold, 2 italic, 32 subscript, 64 superscript

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
                    self._pending.append((x + side * (w + 3.0) + 2.0, y - 1.5, chg))
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
                             f'<t p="{a["x"]:.2f} {a["y"]:.2f}">{runs_xml(a["label"], FONT_ARIAL, a.get("size", 10), 96)}</t></n>')
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
                    self._pending.append((a["x"] + side * (w + 3.0) + 2.0, a["y"] - 1.5, a["charge"]))
                else:
                    attrs += f' Charge="{a["charge"]}"'
            if a.get("labelsize"):
                attrs += f' LabelSize="{a["labelsize"]}"'
            nodes.append(f'<n {attrs}/>')
        bl = [f'<b id="{self.nid()}" B="{ids[i]}" E="{ids[j]}" Order="{o}"{" Display=\"" + d + "\"" if d else ""}/>'
              for (i, j, o, *rest) in bonds for d in [rest[0] if rest else None]]
        self.items.append(f'<fragment id="{self.nid()}">\n' + "\n".join(nodes + bl) + "\n</fragment>")
        self._flush_symbols()
        return ids

    # ---------- text, lines, arrows
    def text(self, x, y, runs, font=FONT_ARIAL, size=10, align=None):
        """runs: str or list of (text, face) with face bits: 1 bold, 2 italic, 32 subscript, 64 superscript."""
        if isinstance(runs, str):
            runs = [(runs, 0)]
        s = "".join(runs_xml(t, font, size, face) for t, face in runs)
        just = f' Justification="{align}"' if align else ""
        self.items.append(f'<t id="{self.nid()}" p="{x:.2f} {y:.2f}"{just}>{s}</t>')

    def line(self, x1, y1, x2, y2, dashed=False, bold=False):
        lt = "Dashed" if dashed else ("Bold" if bold else "Solid")
        self.items.append(f'<graphic id="{self.nid()}" BoundingBox="{x1:.2f} {y1:.2f} {x2:.2f} {y2:.2f}" '
                          f'GraphicType="Line" LineType="{lt}"/>')

    def charge_symbol(self, x, y, sign, size=6.5):
        """ChemDraw circled charge symbol centred at (x, y)."""
        st = "CirclePlus" if sign > 0 else "CircleMinus"
        h = size / 2
        self.items.append(f'<graphic id="{self.nid()}" BoundingBox="{x-h:.2f} {y-h:.2f} {x+h:.2f} {y+h:.2f}" GraphicType="Symbol" SymbolType="{st}"/>')

    def oval(self, x1, y1, x2, y2):
        self.items.append(f'<graphic id="{self.nid()}" BoundingBox="{x1:.2f} {y1:.2f} {x2:.2f} {y2:.2f}" GraphicType="Oval" OvalType="Circle" LineWidth="0.5"/>')

    def arrow(self, x1, y1, x2, y2, head="Full", tail="Unspecified", dashed=False):
        """Straight arrow from tail (x1,y1) to head (x2,y2). head/tail: Full, HalfLeft, HalfRight, Unspecified."""
        lt = ' LineType="Dashed"' if dashed else ""
        self.items.append(f'<arrow id="{self.nid()}" FillType="None" ArrowheadHead="{head}" ArrowheadTail="{tail}" '
                          f'ArrowheadType="Solid" HeadSize="1000" ArrowheadCenterSize="875" ArrowheadWidth="250"{lt} '
                          f'Head3D="{x2:.2f} {y2:.2f} 0" Tail3D="{x1:.2f} {y1:.2f} 0"/>')

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


def runs_xml(t, font, size, face):
    """Split a run so Greek letters are set in the Symbol font (ChemDraw convention; Latin-1 fonts turn beta into sharp s)."""
    out, buf = [], ""
    for ch in t:
        if ch in GREEK:
            if buf: out.append(f'<s font="{font}" size="{size}" face="{face}">{esc(buf)}</s>'); buf = ""
            out.append(f'<s font="{FONT_SYMBOL}" size="{size}" face="{face}">{GREEK[ch]}</s>')
        else:
            buf += ch
    if buf: out.append(f'<s font="{font}" size="{size}" face="{face}">{esc(buf)}</s>')
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
def preview(path, png, scale=3):
    import xml.etree.ElementTree as ET
    import matplotlib; matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    root = ET.parse(path).getroot()
    page = root.find("page")
    W, H = [float(v) for v in page.get("BoundingBox").split()[2:]]
    fig, ax = plt.subplots(figsize=(W/72*scale, H/72*scale), dpi=72)
    ax.set_xlim(0, W); ax.set_ylim(H, 0); ax.set_aspect("equal"); ax.axis("off")
    for frag in page.iter("fragment"):
        pos = {}
        for n in frag.findall("n"):
            x, y = [float(v) for v in n.get("p").split()]
            pos[n.get("id")] = (x, y)
            z = int(n.get("Element", 6))
            if z != 6:
                lab = PT.get(z, str(z)); nh = int(n.get("NumHydrogens", 0)); ch = int(n.get("Charge", 0))
                if nh: lab += "H" + (str(nh) if nh > 1 else "")
                if ch: lab += ("+" if ch > 0 else "–")
                ax.text(x, y, lab, ha="center", va="center", fontsize=10*scale, bbox=dict(fc="white", ec="none", pad=0.5))
        for b in frag.findall("b"):
            (x1, y1), (x2, y2) = pos[b.get("B")], pos[b.get("E")]
            o = b.get("Order", "1")
            ax.plot([x1, x2], [y1, y2], color="k", lw=0.8*scale, zorder=0)
            if o == "2":
                dx, dy = x2-x1, y2-y1; L = (dx*dx+dy*dy)**0.5; nx, ny = -dy/L*2.2, dx/L*2.2
                ax.plot([x1+nx, x2+nx], [y1+ny, y2+ny], color="k", lw=0.8*scale, zorder=0)
            if o == "dative":
                ax.annotate("", xy=(x2, y2), xytext=(x1, y1), arrowprops=dict(arrowstyle="->", lw=0.8*scale))
    for t in page.findall("t"):
        x, y = [float(v) for v in t.get("p").split()]
        s = "".join(st.text or "" for st in t.findall("s"))
        ax.text(x, y, s, ha={"Center": "center", "Right": "right"}.get(t.get("Justification"), "left"), va="baseline", fontsize=10*scale)
    for g in page.findall("graphic"):
        x1, y1, x2, y2 = [float(v) for v in g.get("BoundingBox").split()]
        ax.plot([x1, x2], [y1, y2], color="k", lw=(2.5 if g.get("LineType") == "Bold" else 0.8)*scale,
                ls="--" if g.get("LineType") == "Dashed" else "-")
    for a in page.findall("arrow"):
        hx, hy, _ = [float(v) for v in a.get("Head3D").split()]
        tx, ty, _ = [float(v) for v in a.get("Tail3D").split()]
        ax.annotate("", xy=(hx, hy), xytext=(tx, ty), arrowprops=dict(arrowstyle="->", lw=0.8*scale))
    fig.savefig(png, bbox_inches="tight", facecolor="white"); plt.close(fig)


# ---------- text width in points, Arial metrics (Liberation Sans is metric-compatible)
def text_width(txt, size=10, bold=False):
    from PIL import ImageFont
    f = ImageFont.truetype("/usr/share/fonts/truetype/liberation/LiberationSans-%s.ttf" % ("Bold" if bold else "Regular"), 100)
    return f.getlength(txt) * size / 100.0
