"""Builds the hemoglobin ChemDraw panels as CDXML."""
import math, sys, os
from rdkit import Chem
from cdxml import Doc, mol2d, preview, BL, text_width, SUB, SUP

OUT = sys.argv[1] if len(sys.argv) > 1 else "."

# ------------------------------------------------------------------ panel 1: heme b, top view
def panel1():
    from heme_template import ppix_mol
    m, Ns = ppix_mol()                       # idealized coordinates in angstrom, Fe-free, N without H
    d = Doc()
    nN = m.GetNumAtoms()
    # Fe at the origin of the template; add_mol centers on the all-atom centroid, so pass Fe relative to that
    conf = m.GetConformer()
    cx = sum(conf.GetAtomPosition(i).x for i in range(nN)) / nN
    cy = sum(conf.GetAtomPosition(i).y for i in range(nN)) / nN
    d.add_mol(m, ox=200, oy=185, extra_atoms=[{"sym": "Fe", "x": -cx / 1.40, "y": cy / 1.40}],
              extra_bonds=[(i, nN, "1") for i in Ns], hcount={i: 0 for i in Ns}, unit=1.40,
              nowarn=set(Ns) | {nN})
    d.text(110, 345, "Heme b (Fe-protoporphyrin IX)", size=11)
    d.text(110, 358, "Fe(II) bound by the four pyrrole nitrogens; propionates ionized at pH 7", size=9)
    d.write(os.path.join(OUT, "hb_p01_heme_top.cdxml"), 400, 375)

# ------------------------------------------------------------------ panel 2: heme side view, T vs R
def side_view(d, cx, oxy):
    y_plane = 170.0
    half = 52.0
    off = 0.0 if oxy else 22.0                   # exaggerated Fe displacement (real: 0.4 A)
    fe_y = y_plane - off
    atoms = []; bonds = []
    def A(sym, x, y, **kw):
        atoms.append(dict(sym=sym, x=x, y=y, **kw)); return len(atoms) - 1
    nL = A("N", cx - half, y_plane); nR = A("N", cx + half, y_plane)
    fe = A("Fe", cx, fe_y)
    bonds += [(nL, fe, "1"), (nR, fe, "1")]
    # proximal His F8 imidazole below (NE2 up, bound to Fe)
    r = BL / (2 * math.sin(math.radians(36)))
    fe_n = BL * 1.9                              # axial bonds drawn long so they read past the plane line
    ring_cy = fe_y + fe_n + r
    ids = []
    for k in range(5):
        a = math.radians(-90 + 72 * k)
        x, y = cx + r * math.cos(a), ring_cy + r * math.sin(a)
        sym = "N" if k in (0, 2) else "C"
        ids.append(A(sym, x, y, h=(1 if k == 2 else 0)))
    ne2, ce1, nd1, cg, cd2 = ids
    bonds += [(ne2, ce1, "2"), (ce1, nd1, "1"), (nd1, cg, "1"), (cg, cd2, "2"), (cd2, ne2, "1")]
    bonds.append((fe, ne2, "dative"))
    cb = A("C", atoms[cg]["x"] - BL * math.cos(math.radians(54)), atoms[cg]["y"] + BL * math.sin(math.radians(54)))
    bonds.append((cg, cb, "1"))
    if oxy:
        o1 = A("O", cx, fe_y - fe_n)
        o2 = A("O", cx + BL * math.cos(math.radians(30)), fe_y - fe_n - BL * math.sin(math.radians(30)))
        bonds += [(fe, o1, "1"), (o1, o2, "2")]
    if oxy:   # leave a gap in the plane line so the Fe label reads
        d.line(cx - half, y_plane, cx - 12, y_plane, bold=True)
        d.line(cx + 12, y_plane, cx + half, y_plane, bold=True)
    else:
        d.line(cx - half, y_plane, cx + half, y_plane, bold=True)      # the porphyrin plane, edge-on
    d.add_fragment_raw(atoms, bonds)
    d.text(cx + half + 8, y_plane + 3, "porphyrin", size=8)
    d.text(cx + half + 8, y_plane + 12, "plane", size=8)
    d.text(atoms[cb]["x"] - 44, atoms[cb]["y"] + 4, "His F8", size=9)
    d.text(atoms[cb]["x"] - 44, atoms[cb]["y"] + 14, "(proximal)", size=8)
    if not oxy:
        xd = cx - half - 12
        d.line(xd - 4, fe_y, xd + 4, fe_y)          # ticks
        d.line(xd - 4, y_plane, xd + 4, y_plane)
        d.line(xd, fe_y, xd, y_plane, dashed=True)
        d.text(xd - 34, fe_y - 2, "0.4 Å", size=8)
        d.text(cx - 70, 70, [("T state (deoxy)", 1)], size=10)
        d.text(cx - 70, 84, "5-coordinate Fe, out of the plane", size=8)
        d.text(cx - 70, 95, "toward His F8 (exaggerated here)", size=8)
    else:
        d.text(cx - 70, 70, [("R state (oxy)", 1)], size=10)
        d.text(cx - 70, 84, "6-coordinate Fe, in the plane;", size=8)
        d.text(cx - 70, 95, [("O", 0), ("2", SUB), (" bound on the distal side", 0)], size=8)

def panel2():
    d = Doc()
    side_view(d, 150, oxy=False)
    side_view(d, 430, oxy=True)
    d.line(292, 60, 292, 285, dashed=True)
    d.write(os.path.join(OUT, "hb_p02_heme_side.cdxml"), 580, 300)

# ------------------------------------------------------------------ helpers
def orient(mol, i, j, want_below=None):
    """rotation (deg, RDKit frame) that puts atom i->j along +x; returns (rot, mirror) so that atom want_below ends up below on the page."""
    c = mol.GetConformer(); a, b = c.GetAtomPosition(i), c.GetAtomPosition(j)
    rot = -math.degrees(math.atan2(b.y - a.y, b.x - a.x))
    mirror = False
    if want_below is not None:
        t = math.radians(rot); p = c.GetAtomPosition(want_below)
        y = (p.x - a.x) * math.sin(t) + (p.y - a.y) * math.cos(t)      # RDKit y after rotation (page: positive = up)
        mirror = y > 0
    return rot, mirror

S30, C30 = math.sin(math.radians(30)), math.cos(math.radians(30))

def residue(d, kind, ox, oy):
    """Backbone N-CA-C(=O) left to right starting at (ox, oy); side chain down. Returns (x_min, x_max)."""
    A = []; B = []
    def add(sym, x, y, **kw): A.append(dict(sym=sym, x=x, y=y, **kw)); return len(A) - 1
    left = add("X", ox - BL, oy, label="Pro–Thr–Leu–His–Val")   # stored from the attachment outward; ChemDraw displays Val–His–Leu–Thr–Pro–N
    n  = add("N", ox, oy, h=1)
    ca = add("C", ox + BL * C30, oy + BL * S30)
    c  = add("C", ox + 2 * BL * C30, oy)
    o  = add("O", ox + 2 * BL * C30, oy - BL)
    right = add("X", ox + 3 * BL * C30, oy + BL * S30, label="Glu–Lys")   # 120 deg from the C=O and the CA bond
    B += [(left, n, "1"), (n, ca, "1"), (ca, c, "1"), (c, o, "2"), (c, right, "1")]
    cax, cay = A[ca]["x"], A[ca]["y"]
    if kind == "Glu":
        cb = add("C", cax, cay + BL)
        cg = add("C", cax + BL * C30, cay + BL + BL * S30)
        cd = add("C", cax + BL * C30, cay + 2 * BL + BL * S30)
        o1 = add("O", cax + 2 * BL * C30, cay + 2 * BL + 2 * BL * S30)
        o2 = add("O", cax, cay + 2 * BL + 2 * BL * S30, charge=-1)
        B += [(ca, cb, "1"), (cb, cg, "1"), (cg, cd, "1"), (cd, o1, "2"), (cd, o2, "1")]
    else:  # Val
        cb = add("C", cax, cay + BL)
        g1 = add("C", cax - BL * C30, cay + BL + BL * S30)
        g2 = add("C", cax + BL * C30, cay + BL + BL * S30)
        B += [(ca, cb, "1"), (cb, g1, "1"), (cb, g2, "1")]
    d.add_fragment_raw(A, B)
    return min(a["x"] for a in A), max(a["x"] for a in A)

# ------------------------------------------------------------------ panel 3: Glu6 vs Val6
def panel3():
    d = Doc()
    def row(y, kind, title, note):
        d.text(30, y - 44, [(title, 1)], size=10)
        d.text(30, y - 32, note, size=8)
        xr = 30 + text_width("Val–His–Leu–Thr–Pro", 10) + BL + 4   # label hangs to the left of its node
        x0, x1 = residue(d, kind, xr, y)
        d.text(xr + 2, y + (84 if kind == "Glu" else 52), "position 6", size=8)
    row(90, "Glu", "Hb A (normal): Glu6", "glutamate: carboxylate, charged, keeps the surface hydrophilic")
    row(236, "Val", "Hb S (sickle): Val6", "valine: isopropyl, hydrophobic; docks into the Phe85/Leu88 pocket of a neighbouring tetramer")
    d.text(30, 312, "β-chain N-terminal sequence; only residue 6 is drawn", size=8)
    d.write(os.path.join(OUT, "hb_p03_glu_val.cdxml"), 420, 330)

# ------------------------------------------------------------------ panel 4: Bohr pair + equilibrium
def panel4():
    d = Doc()
    A = []; B = []
    def add(sym, x, y, **kw): A.append(dict(sym=sym, x=x, y=y, **kw)); return len(A) - 1
    # His146beta imidazolium: pentagon centred at (cx, cy), CG at the left, NE2 at the lower right
    cx, cy = 110, 95
    r = BL / (2 * math.sin(math.radians(36)))
    ang = {"CG": 180, "CD2": 252, "NE2": 324, "CE1": 36, "ND1": 108}
    idx = {}
    for name, a in ang.items():
        sym = "N" if name.startswith("N") else "C"
        idx[name] = add(sym, cx + r * math.cos(math.radians(a)), cy - r * math.sin(math.radians(a)),
                        h=(1 if name in ("NE2", "ND1") else 0), charge=(1 if name == "NE2" else 0))
    cb = add("C", A[idx["CG"]]["x"] - BL, A[idx["CG"]]["y"])
    B += [(idx["CG"], idx["CD2"], "2"), (idx["CD2"], idx["NE2"], "1"), (idx["NE2"], idx["CE1"], "2"),
          (idx["CE1"], idx["ND1"], "1"), (idx["ND1"], idx["CG"], "1"), (idx["CG"], cb, "1")]
    # Asp94beta carboxylate to the right, O- facing the NE2-H
    nx, ny = A[idx["NE2"]]["x"], A[idx["NE2"]]["y"]
    ox, oy = nx + 3.4 * BL + 3, ny
    o2 = add("O", ox, oy, charge=-1)
    cg = add("C", ox + BL * C30, oy - BL * S30)
    o1 = add("O", ox + BL * C30, oy - BL * S30 - BL)
    cb2 = add("C", ox + 2 * BL * C30, oy)
    B += [(o2, cg, "1"), (cg, o1, "2"), (cg, cb2, "1")]
    d.add_fragment_raw(A, B)
    d.line(nx + 22, ny, ox - 20, oy, dashed=True)
    d.text(cx - 70, cy + 42, "His HC3 (146β)", size=9); d.text(cx - 70, cy + 53, "imidazolium;", size=8); d.text(cx - 70, cy + 63, "pKa raised in T", size=8)
    d.text(ox + 6, cy + 42, "Asp FG1 (94β)", size=9); d.text(ox + 6, cy + 53, "carboxylate", size=8)
    d.text(cx - 20, cy - 40, "ion pair: intact in T, broken in R", size=8)
    # Bohr equilibrium (Lehninger)
    y = 205
    d.text(50, y + 4, [("HHb", 0), ("+", SUP), ("  +  O", 0), ("2", SUB)], size=11)
    d.equilibrium(135, 185, y)
    d.text(200, y + 4, [("HbO", 0), ("2", SUB), ("  +  H", 0), ("+", SUP)], size=11)
    d.text(45, y + 22, "tissues: low pH,", size=8); d.text(45, y + 32, [("high CO", 0), ("2", SUB)], size=8)
    d.text(200, y + 22, "lungs: pH 7.4,", size=8); d.text(200, y + 32, [("high pO", 0), ("2", SUB)], size=8)
    d.text(50, y + 52, [("Protonating His146 stabilizes the T-state ion pair and releases O", 0), ("2", SUB), (" (Bohr effect)", 0)], size=8)
    d.write(os.path.join(OUT, "hb_p04_bohr_pair.cdxml"), 420, 280)

if __name__ == "__main__":
    panel1(); panel2(); panel3(); panel4()
    for n in ("hb_p01_heme_top", "hb_p02_heme_side", "hb_p03_glu_val", "hb_p04_bohr_pair"):
        preview(os.path.join(OUT, n + ".cdxml"), os.path.join(OUT, n + "_preview.png"))
    print("done")
