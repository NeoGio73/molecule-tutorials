"""Idealized D4h porphyrin coordinates (angstrom) for protoporphyrin IX, rings at the corners."""
import math
from rdkit import Chem
from rdkit.Geometry import Point3D

def rot(p, deg):
    t = math.radians(deg); x, y = p
    return (x*math.cos(t) - y*math.sin(t), x*math.sin(t) + y*math.cos(t))

def build():
    atoms = []   # (sym, x, y, charge)
    bonds = []   # (i, j)
    def A(sym, x, y, charge=0):
        atoms.append((sym, x, y, charge)); return len(atoms)-1
    # ring k faces direction angle 90*k (0=right, 90=up ...); substituent pattern per ring, clockwise order later
    rings = []
    for k in range(4):
        ang = 90*k
        N  = A("N", *rot((2.00, 0.00), ang))
        Ca1 = A("C", *rot((2.85, 1.10), ang)); Ca2 = A("C", *rot((2.85, -1.10), ang))
        Cb1 = A("C", *rot((4.23, 0.68), ang)); Cb2 = A("C", *rot((4.23, -0.68), ang))
        bonds += [(N,Ca1),(N,Ca2),(Ca1,Cb1),(Ca2,Cb2),(Cb1,Cb2)]
        rings.append(dict(N=N, Ca1=Ca1, Ca2=Ca2, Cb1=Cb1, Cb2=Cb2, ang=ang))
    # meso carbons between ring k (Ca1 side, +angle) and ring k+1 (Ca2 side)
    for k in range(4):
        m = A("C", *rot((2.40, 2.40), 90*k))
        bonds += [(rings[k]["Ca1"], m), (rings[(k+1)%4]["Ca2"], m)]
    # substituents: outward direction from Fe through Cb
    def sub(cb, kind, side):
        x, y = atoms[cb][1], atoms[cb][2]
        L = math.hypot(x, y); d = (x/L, y/L)
        def step(p, dirv, deg, n=1.50):
            dv = rot(dirv, deg); return (p[0]+dv[0]*n, p[1]+dv[1]*n), dv
        p0 = (x, y)
        if kind == "Me":
            c = A("C", *step(p0, d, 0)[0]); bonds.append((cb, c))
        elif kind == "vinyl":
            p1, _ = step(p0, d, 0); c1 = A("C", *p1); bonds.append((cb, c1))
            p2, _ = step(p1, d, 60*side); c2 = A("C", *p2); bonds.append((c1, c2))
        elif kind == "prop":
            p1, _ = step(p0, d, 0);        c1 = A("C", *p1); bonds.append((cb, c1))
            p2, _ = step(p1, d, 60*side);  c2 = A("C", *p2); bonds.append((c1, c2))
            p3, _ = step(p2, d, 0);        c3 = A("C", *p3); bonds.append((c2, c3))
            p4, _ = step(p3, d, 60*side);  o1 = A("O", *p4); bonds.append((c3, o1))
            p5, _ = step(p3, d, -60*side); o2 = A("O", *p5, charge=-1); bonds.append((c3, o2))
    # PPIX pattern after a 45 deg rotation rings sit at corners; ring index 0 faces right (will become top-right = ring B)
    # Going clockwise on the page (y up here): ring1 (up) -> A, ring0 (right) -> B, ring3 (down) -> C, ring2 (left) -> D
    # Each ring: Cb1 is on the +angle (counter-clockwise) side, Cb2 on the clockwise side.
    # clockwise order within a ring = Cb1 then Cb2.
    pattern = {1: ("Me", "vinyl"), 0: ("Me", "vinyl"), 3: ("Me", "prop"), 2: ("prop", "Me")}
    for k, (s1, s2) in pattern.items():
        sub(rings[k]["Cb1"], s1, +1); sub(rings[k]["Cb2"], s2, -1)
    # rotate whole thing by 45 deg so rings sit at the corners
    atoms = [(s, *rot((x, y), 45), c) for (s, x, y, c) in atoms]
    return atoms, bonds, [r["N"] for r in rings]

def ppix_mol():
    """RDKit PPIX (carboxylates, no NH) with template coordinates in angstrom, plus the N indices."""
    atoms, bonds, Ns = build()
    rw = Chem.RWMol()
    for s_, x, y, c in atoms:
        a = Chem.Atom(s_); a.SetFormalCharge(c); a.SetNoImplicit(True); rw.AddAtom(a)
    for i, j in bonds: rw.AddBond(i, j, Chem.BondType.SINGLE)
    # core = first 24 atoms (4 rings x 5 + 4 meso); match a kekulized porphine onto it to copy bond orders
    core = Chem.RWMol()
    for k in range(24):
        a = Chem.Atom(atoms[k][0]); core.AddAtom(a)
    for i, j in bonds:
        if i < 24 and j < 24: core.AddBond(i, j, Chem.BondType.SINGLE)
    params = Chem.AdjustQueryParameters.NoAdjustments(); params.makeBondsGeneric = True
    q = Chem.AdjustQueryProperties(core.GetMol(), params)
    porphine = Chem.MolFromSmiles("c1cc2cc3ccc(cc4ccc(cc5ccc(cc1n2)[nH]5)n4)[nH]3")
    Chem.Kekulize(porphine, clearAromaticFlags=True)
    match = porphine.GetSubstructMatch(q)        # template core atom i -> porphine atom match[i]
    assert len(match) == 24
    inv = {match[i]: i for i in range(24)}
    for b in porphine.GetBonds():
        i, j = inv[b.GetBeginAtomIdx()], inv[b.GetEndAtomIdx()]
        rw.GetBondBetweenAtoms(i, j).SetBondType(b.GetBondType())
    # substituents: vinyl C=C and C=O
    for i, j in bonds:
        if i >= 24 and j >= 24:
            si, sj = atoms[i][0], atoms[j][0]
            ni = rw.GetAtomWithIdx(i); nj = rw.GetAtomWithIdx(j)
            if si == "C" and sj == "C" and ni.GetDegree() == 2 and nj.GetDegree() == 1:   # vinyl terminal
                rw.GetBondBetweenAtoms(i, j).SetBondType(Chem.BondType.DOUBLE)
            if si == "C" and sj == "O" and atoms[j][3] == 0:                            # C=O of carboxylate
                rw.GetBondBetweenAtoms(i, j).SetBondType(Chem.BondType.DOUBLE)
    m = rw.GetMol()
    for a in m.GetAtoms():
        a.SetNoImplicit(False)
    m.UpdatePropertyCache(strict=False)
    conf = Chem.Conformer(m.GetNumAtoms())
    for k, (s_, x, y, c) in enumerate(atoms): conf.SetAtomPosition(k, Point3D(x, y, 0))
    m.AddConformer(conf)
    return m, Ns

if __name__ == "__main__":
    m, Ns = ppix_mol(); print(m.GetNumAtoms(), Ns)
