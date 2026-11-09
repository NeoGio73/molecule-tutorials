"""Ion-pair distances for Lehninger Fig. 5-9 at one interface, measured the usual way for a salt bridge: the closest
N…O distance between the two charged groups, in each state separately (paper Table 2 uses the same convention).
T state: 2HHB (alpha1 = A, beta1 = B, alpha2 = C). R state: 1HHO (alpha1 = A, beta1 = B; alpha2 = A' made with the
entry's BIOMT operator 2).

Usage: python3 ion_pair_distances.py 2HHB.pdb 1HHO.pdb out.json"""
import json, re, sys
import numpy as np

HIS_N, ASP_O, ARG_N, LYS_N, CTERM_O, NTERM_N = ["ND1", "NE2"], ["OD1", "OD2"], ["NE", "NH1", "NH2"], ["NZ"], ["O", "OXT"], ["N"]
# (name as in hemoglobin.json, (chain, residue, atoms) in T, the partner in T, chain letters in R for the two groups)
PAIRS = [
    ("His146β–Asp94β (intrachain)", ("B", 146, HIS_N), ("B", 94, ASP_O), ("B", "B")),
    ("His146β carboxylate–Lys40α", ("B", 146, CTERM_O), ("C", 40, LYS_N), ("B", "A'")),
    ("Arg141α guanidinium–Asp126α′", ("A", 141, ARG_N), ("C", 126, ASP_O), ("A", "A'")),
    ("Arg141α carboxylate–Val1α′ amine", ("A", 141, CTERM_O), ("C", 1, NTERM_N), ("A", "A'")),
]


def load(path):
    atoms, biomt = {}, {}
    for line in open(path):
        if line.startswith(("ATOM", "HETATM")):
            atoms[(line[21], int(line[22:26]), line[12:16].strip())] = np.array([float(line[30:38]), float(line[38:46]), float(line[46:54])])
        m = re.match(r"REMARK 350\s+BIOMT(\d)\s+(\d+)\s+(.*)", line)
        if m:
            biomt.setdefault(int(m.group(2)), {})[int(m.group(1))] = [float(x) for x in m.group(3).split()]
    return atoms, biomt


def closest(atoms, a, b, op_b=None):
    best = None
    for na in a[2]:
        for nb in b[2]:
            p, q = atoms.get((a[0], a[1], na)), atoms.get((b[0], b[1], nb))
            if p is None or q is None: continue
            if op_b is not None: q = op_b[0] @ q + op_b[1]
            d = float(np.linalg.norm(p - q))
            if best is None or d < best[0]: best = (d, na, nb)
    return best


if __name__ == "__main__":
    T, _ = load(sys.argv[1]); R, bm = load(sys.argv[2])
    op = (np.array([bm[2][i][:3] for i in (1, 2, 3)]), np.array([bm[2][i][3] for i in (1, 2, 3)]))
    out = {}
    for name, a, b, (ra, rb) in PAIRS:
        dT = closest(T, a, b)
        ar, br = (ra.rstrip("'"), a[1], a[2]), (rb.rstrip("'"), b[1], b[2])
        dR = closest(R, ar, br, op if rb.endswith("'") else None)
        out[name] = {"distT": round(dT[0], 2), "atomsT": dT[1:], "distR": round(dR[0], 2), "atomsR": dR[1:]}
        print(f"{name}: T {dT[0]:.2f} ({dT[1]}…{dT[2]})  R {dR[0]:.2f} ({dR[1]}…{dR[2]})")
    json.dump(out, open(sys.argv[3], "w"), ensure_ascii=False, indent=1)
