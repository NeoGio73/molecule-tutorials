"""Chymotrypsin mechanism frames as CDXML, laid out after Lehninger Fig. 6-27 (the course edition's number).
Layout as in the book: His57 and Ser195 above the substrate, Ser O right over the scissile carbonyl C, the oxyanion hole
below it, the hydrophobic pocket at lower right, the leaving (amine) half on the left. The substrate backbone is in the
book's condensed style, AAn-C-CH-NH-C-CH-NH-AAn, with C=O, R1 and the benzyl group hanging below.
Colors as in the book: leaving half blue, acyl half black, the proton that moves magenta, arrows blue.
Agreed additions to the book's figure (Nov 7): a third arrow in the ES complex (His57 N takes the Ser H), and Asp102.
House conventions: formal charges circled; lone pairs only where an arrow starts.
Usage: python3 make_chymo.py outdir [state ...]"""
import math, os, sys
from cdxml import Doc, preview, BL, SUB, SUP, text_width
from make_panels import res_runs

B, M = "blue", "magenta"
Y0, XC = 192.0, 150.0             # substrate line; x of the scissile carbon C1
GAP = 11.0                        # between neighbouring condensed labels (ChemDraw shows a dash of about GAP - 3.2)
DY = 19.0                         # label-to-label drop for C=O, CH-R and the Ser O above C1
SUBW = 0.75                       # sub/superscript size relative to normal (ChemDraw sets 7.5 pt under 10 pt)

STATES = {   # state: (file, title)
    "free":  ("ch_p01_free_enzyme", "Free enzyme"),
    "es":    ("ch_p03_es_complex", "ES complex"),
    "ti1":   ("ch_p04_intermediate_acylation", "Short-lived intermediate (acylation)"),
    "acyl":  ("ch_p05_acyl_enzyme", "Acyl-enzyme intermediate"),
    "water": ("ch_p06_acyl_enzyme_water", "Acyl-enzyme intermediate"),
    "ti2":   ("ch_p07_intermediate_deacylation", "Short-lived intermediate (deacylation)"),
    "ep2":   ("ch_p07b_enzyme_product2", "Enzyme–product 2 complex"),
}


def w(t): return text_width(t, 10)


def runs_width(runs): return sum(w(r[0]) * (SUBW if (r[1] & (SUB | SUP)) else 1) for r in runs)


def ext(runs, reversed_=False):
    """(left, right) extent of a label around its node; ChemDraw centres the attaching character on the node."""
    tot = runs_width(runs)
    if reversed_:                 # left-hand chain end, shown reversed: the attaching token closes the text
        att = w("A") / 2 + w("n") * SUBW
        return tot - att, att
    first = w(runs[0][0][0])
    return first / 2, tot - first / 2


def polar(x, y, deg, r=BL):
    return x + r * math.cos(math.radians(deg)), y + r * math.sin(math.radians(deg))


def unit(ax, ay, bx, by):
    L = math.hypot(bx - ax, by - ay); return (bx - ax) / L, (by - ay) / L


def ang(ax, ay, bx, by): return math.degrees(math.atan2(by - ay, bx - ax))


class Frag:
    def __init__(self): self.A, self.B = [], []
    def add(self, sym, x, y, **kw): self.A.append(dict(sym=sym, x=x, y=y, **kw)); return len(self.A) - 1
    def bond(self, i, j, order="1", color=None):
        self.B.append((i, j, order, None, color) if color else (i, j, order))
    def xy(self, i): return self.A[i]["x"], self.A[i]["y"]


# ---- backbone x positions, fixed for every frame: spacing follows the label widths so every dash is the same length
AAN = [("AA", 0), ("n", SUB)]
AAN_LEFT = [("A", 0), ("n", SUB), ("A", 0)]          # ChemDraw reverses a left-hand label: stored A(n)A shows as AAn
BACKBONE = [("L0", AAN_LEFT, True), ("C3", [("C", 0)], False), ("CA2", [("CH", 0)], False), ("N2", [("NH", 0)], False),
            ("C1", [("C", 0)], False), ("CA1", [("CH", 0)], False), ("N1", [("NH", 0)], False), ("L1", AAN, False)]
X = {}
_x, _prev = 0.0, None
for key, runs, rev in BACKBONE:
    l, r = ext(runs, rev)
    if _prev is not None: _x += _prev + GAP + l
    X[key] = _x; _prev = r
_shift = XC - X["C1"]
X = {k: v + _shift for k, v in X.items()}
OH_DX = w("O") / 2 + GAP + w("H") / 2                 # O-H and O-C spacing in the condensed style
OS = (XC, Y0 - 24)                                     # Ser195 O, right over C1 in every frame
NE2 = (X["N2"] - 6, Y0 - 50)                           # His57 N-epsilon2, above the leaving NH
RR = BL / (2 * math.sin(math.radians(36)))             # imidazole circumradius


def frame(d, st):
    f = Frag()
    sub = st != "free"
    ti = st in ("ti1", "ti2")
    whole = st in ("es", "ti1")                        # leaving half still bonded
    serC = st in ("ti1", "acyl", "water", "ti2")       # Ser O bonded to C1
    L = {}                                             # fragment node indices by name
    P = {}                                             # substrate groups written as text: name -> (x, y, left, right)
    lab = lambda key, x, y, runs, color=None: f.add("X", x, y, label=runs, color=color)   # residue names (one bond each)

    # The substrate is drawn the way the book typesets it: each group (C, CH, NH, AAn, O, R1) is plain text with its
    # attaching letter centred on (x, y), and the bonds are plain lines between the letters. ChemDraw stacks the H of a
    # CH or NH label that has bonds on both sides, and its SVG export then misplaces the C or N (seen in ch_p03, Nov 7).
    def atom(name, x, y, runs, color=None, rev=False):
        l, r = ext(runs, rev)
        d.text(x - l, y + 3.75, AAN if rev else runs, color=color)
        P[name] = (x, y, l, r)

    def bond(a, b, order="1", color=None):
        (ax_, ay_, al, ar), (bx_, by_, bl, br) = P[a], P[b]
        if abs(ay_ - by_) < 0.1:                       # horizontal
            if ax_ > bx_: (ax_, ay_, al, ar), (bx_, by_, bl, br) = (bx_, by_, bl, br), (ax_, ay_, al, ar)
            d.line(ax_ + ar + 2.0, ay_, bx_ - bl - 2.0, by_, color=color)
        else:                                          # vertical, under the attaching letter
            y1, y2 = sorted((ay_, by_))
            for o in ((-1.3, 1.3) if order == "2" else (0.0,)):
                d.line(ax_ + o, y1 + 5.4, ax_ + o, y2 - 5.4, color=color)

    # ---------------- substrate
    if sub:
        atom("C1", XC, Y0, [("C", 0)])
        atom("CA1", X["CA1"], Y0, [("CH", 0)])
        atom("N1", X["N1"], Y0, [("NH", 0)])
        atom("L1", X["L1"], Y0, AAN)
        bond("C1", "CA1"); bond("CA1", "N1"); bond("N1", "L1")
        atom("O1", XC, Y0 + DY, [("O", 0)])
        bond("C1", "O1", "1" if ti else "2")
        if ti:
            d.charge_symbol(XC - 9, Y0 + DY - 1, -1)
        # benzyl down into the pocket: a line from the CH to the CH2 corner, then the ring
        cax = X["CA1"]
        cb = (cax + 8, Y0 + DY)
        t0 = 5.4 / (cb[1] - Y0)
        d.line(cax + t0 * (cb[0] - cax), Y0 + 5.4, *cb)
        CB = f.add("C", *cb)
    cax = X["CA1"]
    rc = (cax + 34, Y0 + 34)
    ring_pts = [polar(rc[0], rc[1], -150 + 60 * k) for k in range(6)]
    if sub:
        ring = [f.add("C", *p) for p in ring_pts]
        for k in range(6):
            f.bond(ring[k], ring[(k + 1) % 6], "2" if k % 2 else "1")
        f.bond(CB, ring[0])
    xs = [p[0] for p in ring_pts]; ys = [p[1] for p in ring_pts]
    pl, pr, pt, pb = min(xs) - 7, max(xs) + 7, Y0 + 27, max(ys) + 7     # open at the top, where the side chain comes in
    d.line(pl, pt, pl, pb); d.line(pl, pb, pr, pb); d.line(pr, pb, pr, pt)
    d.text(pr + 5, (pt + pb) / 2 - 2, "hydrophobic", size=8)
    d.text(pr + 5, (pt + pb) / 2 + 7.5, "pocket", size=8)

    # leaving half: bonded (es, ti1) or drifting off as product 1 (acyl)
    if whole or st == "acyl":
        dx = 0 if whole else -14
        atom("L0", X["L0"] + dx, Y0, AAN_LEFT, B, rev=True)
        atom("C3", X["C3"] + dx, Y0, [("C", 0)], B)
        atom("CA2", X["CA2"] + dx, Y0, [("CH", 0)], B)
        atom("N2", X["N2"] + dx, Y0, [("NH", 0)] if whole else [("NH", 0, B), ("H", 0, M)], B)   # product 1 carries the His57 proton
        atom("O3", X["C3"] + dx, Y0 + DY, [("O", 0)], B)
        atom("R1", X["CA2"] + dx, Y0 + DY, [("R", 0), ("1", SUP)], B)
        bond("L0", "C3", color=B); bond("C3", "CA2", color=B); bond("CA2", "N2", color=B)
        bond("C3", "O3", "2", B); bond("CA2", "R1", color=B)
        if whole:
            bond("N2", "C1")
        else:
            d.text(X["C3"] + dx - 4, Y0 + DY + 22, [("Product 1", 1)], size=9, color=B)

    # water (water frame), its O on C1 (ti2), or the carboxylate O- of product 2 (ep2)
    if st == "water":
        L["OW"] = f.add("O", X["N2"] + 2, Y0)
        hw = polar(*f.xy(L["OW"]), ang(*f.xy(L["OW"]), *NE2), 19)
        L["HW"] = f.add("H", *hw, color=M); f.bond(L["OW"], L["HW"])
        L["HW2"] = f.add("H", *polar(*f.xy(L["OW"]), ang(*f.xy(L["OW"]), *NE2) - 110, 18)); f.bond(L["OW"], L["HW2"])
    if st in ("ti2", "ep2"):
        atom("OW", XC - OH_DX, Y0, [("O", 0)])
        bond("OW", "C1")
        if st == "ep2":
            d.charge_symbol(XC - OH_DX - 6, Y0 - 9, -1)
        else:
            atom("HW2", XC - 2 * OH_DX, Y0, [("H", 0)]); bond("HW2", "OW")

    # ---------------- Ser195
    L["OS"] = f.add("O", *OS, nowarn=serC)
    SER = lab("SER", OS[0] + w("O") / 2 + GAP + w("S") / 2, OS[1], res_runs("Ser195"))
    f.bond(L["OS"], SER)
    if st in ("free", "es", "ep2"):
        L["HS"] = f.add("H", OS[0] - OH_DX, OS[1], **({"color": M} if st != "free" else {}))
        f.bond(L["OS"], L["HS"])
    if serC:
        d.line(XC, Y0 - 5.4, XC, OS[1] + 5.4)         # Ser O to C1 (C1 is text, so this bond is a plain line)

    # ---------------- His57: N-epsilon2 at the bottom of the ring, N-delta1-H at upper left, CH2 to the label at upper right
    hc = (NE2[0], NE2[1] - RR)
    pv = lambda a: polar(hc[0], hc[1], a, RR)
    plus = ti
    if st == "ti1":
        hh = polar(*NE2, ang(*NE2, X["N2"], Y0), 19)                     # proton on its way to the leaving N
        chg = (NE2[0] - 9.5, NE2[1] + 3)                                  # left of N: the arrow onto N comes in from the right
    elif st == "ti2":
        hh = polar(*NE2, ang(*NE2, *OS), 19)                              # proton on its way back to Ser O
        chg = (NE2[0] - 6, NE2[1] + 8.5)
    L["NE2"] = f.add("N", *NE2, **({"charge": 1, "charge_at": chg} if plus else {}))
    CE1 = f.add("C", *pv(162)); ND1 = f.add("N", *pv(234)); CG = f.add("C", *pv(306)); CD2 = f.add("C", *pv(18))
    f.bond(L["NE2"], CE1, "2"); f.bond(CE1, ND1); f.bond(ND1, CG); f.bond(CG, CD2, "2"); f.bond(CD2, L["NE2"])
    HIS = lab("HIS", *polar(*pv(306), 306), res_runs("His57")); f.bond(CG, HIS)
    hd1 = polar(*pv(234), 234, 1.2 * BL)
    HD1 = f.add("H", *hd1); f.bond(ND1, HD1)
    if plus:
        L["HH"] = f.add("H", *hh, color=M); f.bond(L["NE2"], L["HH"])

    # ---------------- Asp102 beyond N-delta1-H
    od1 = polar(*hd1, 234, 1.4 * BL)
    OD1 = f.add("O", *od1, charge=-1, charge_at=(od1[0] + 7, od1[1] - 6))
    cga = polar(*od1, 210); CGa = f.add("C", *cga)
    OD2 = f.add("O", *polar(*cga, 150))
    ASP = lab("ASP", *polar(*cga, 270), res_runs("Asp102"))
    f.bond(OD1, CGa); f.bond(CGa, OD2, "2"); f.bond(CGa, ASP)

    # ---------------- oxyanion hole: backbone N-H of Gly193 and Ser195 below O1; names behind the N
    o1 = (XC, Y0 + DY)
    hole = []
    for nx, name in ((XC - 21, "Gly193"), (XC + 21, "Ser195")):    # N-H upright as in the book, stubs down either side
        ny = o1[1] + 38
        a = -90.0
        n_ = f.add("N", nx, ny)
        h_ = f.add("H", *polar(nx, ny, a, 1.2 * BL)); f.bond(n_, h_)
        for t in (a + 120, a - 120):
            s_ = f.add("C", *polar(nx, ny, t)); f.bond(n_, s_)
        hole.append((nx, ny, a, name, f.xy(h_)))

    d.add_fragment_raw(f.A, f.B)

    for nx, ny, a, name, _ in hole:
        d.text(nx, ny + 20, res_runs(name), size=9, align="Center")                  # behind the N, opposite its H
    d.text(XC, o1[1] + 38 + 32, "oxyanion hole", size=8, align="Center")

    # ---------------- hydrogen bonds (dashed), shortened so they stop short of the labels
    def hb(p, q, a0=4.5, b0=5.5):
        ux, uy = unit(*p, *q)
        d.line(p[0] + a0 * ux, p[1] + a0 * uy, q[0] - b0 * ux, q[1] - b0 * uy, dashed=True)
    hb(hd1, od1)                                                          # His57 N-delta1-H ... Asp102 (every frame)
    if st in ("free", "ep2"):                                             # (es, water: the arrow takes the dashes' place)
        hb(f.xy(L["HS"]), NE2)                                            # Ser195 O-H ... His57 N-epsilon2
    if st == "ti1":
        hb(f.xy(L["HH"]), (X["N2"], Y0))
    if st == "ti2":
        hb(f.xy(L["HH"]), OS)
    if sub:
        for i, (nx, ny, a, name, hp) in enumerate(hole):
            if ti or i == 1:                                              # Ser195 N-H always; Gly193 joins for the oxyanion
                hb(hp, o1)

    # ---------------- arrows (Lehninger's steps 2-6), each from a lone pair or a bond
    def lp_toward(p, q, r=6.5):
        ux, uy = unit(*p, *q); c = (p[0] + r * ux, p[1] + r * uy); d.lone_pair(c[0], c[1], ux, uy); return c
    if st in ("es", "water"):
        tgt = f.xy(L["HS"]) if st == "es" else f.xy(L["HW"])
        c = lp_toward(NE2, tgt)                                           # His57 N takes the proton
        ux, uy = unit(*NE2, *tgt)
        d.curved_arrow((c[0] + 3 * ux + 2.5 * uy, c[1] + 3 * uy - 2.5 * ux), (tgt[0] - 4.5 * ux + 3 * uy, tgt[1] - 4.5 * uy - 3 * ux),
                       sweep=100, side=1, color=B)
    if st == "es":
        c = lp_toward(OS, polar(*OS, 62))                                 # Ser O attacks the carbonyl C
        d.curved_arrow((c[0] + 2, c[1] + 2.5), (XC + 3.5, Y0 - 6), sweep=100, side=-1, color=B)
    if st == "water":
        ow = f.xy(L["OW"])
        c = lp_toward(ow, (XC, Y0 - 3))                                   # water O attacks the carbonyl C
        d.curved_arrow((c[0] + 1, c[1] - 3), (XC - 5, Y0 - 5), sweep=110, side=-1, color=B)
    if st in ("es", "water"):
        d.curved_arrow((XC + 3, Y0 + 8.5), (XC + 6.5, Y0 + DY - 2), sweep=160, side=-1, color=B)      # C=O pi onto O
    if ti:
        d.curved_arrow((XC + 6, Y0 + DY + 1.5), (XC + 3.5, Y0 + 9.5), sweep=160, side=1, color=B)     # O- re-forms C=O
        hp = f.xy(L["HH"]); mx, my = (hp[0] + NE2[0]) / 2, (hp[1] + NE2[1]) / 2
        ux, uy = unit(*hp, *NE2)
        d.curved_arrow((mx - 2.5 * uy, my + 2.5 * ux), (NE2[0] + 6, NE2[1] - 3), sweep=150, side=1, color=B)   # H-N bond onto N
    if st == "ti1":
        hp = f.xy(L["HH"])
        d.curved_arrow((XC - 9, Y0 - 2.5), (hp[0] + 4.5, hp[1] + 1), sweep=80, side=1, color=B)      # C-N bond takes the proton
    if st == "ti2":
        hp = f.xy(L["HH"])
        d.curved_arrow((XC - 2.5, Y0 - 12), (hp[0] + 2, hp[1] + 4.5), sweep=80, side=-1, color=B)    # C-O(Ser) bond takes the proton

    title = STATES[st][1]
    d.text(X["L0"] - 12, 42, [(title, 1)], size=11)


if __name__ == "__main__":
    out = sys.argv[1] if len(sys.argv) > 1 else "."
    for st in (sys.argv[2:] or list(STATES)):
        d = Doc()
        frame(d, st)
        path = os.path.join(out, STATES[st][0] + ".cdxml")
        d.write(path, 330, 300)
        preview(path, path.replace(".cdxml", "_preview.png"))
        print("wrote", path)
