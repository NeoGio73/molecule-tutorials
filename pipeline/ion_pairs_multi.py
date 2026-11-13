"""Ion pairs of Lehninger Fig. 5-9 in several hemoglobin crystal structures: closest N...O between the charged groups,
both symmetry-related interfaces, with B factors, occupancies and alternate conformations of the atoms involved, and
the nearest water/ion to the Arg141a C-terminus and the Val1a N-terminus (a possible bridge).

T entries: chains A = alpha1, B = beta1, C = alpha2, D = beta2.
R entries (1HHO, 2DN1): one alpha-beta dimer in the file (A, B); the other dimer made with REMARK 350 BIOMT operator 2.
Usage: python3 -I ion_pairs_multi.py <dir with the .pdb files> T:2HHB T:2DN2 T:1A3N R:1HHO R:2DN1"""
import re, sys, os
import numpy as np

ARG_N, ASP_O, LYS_N, HIS_N, CTERM_O, NTERM_N = ["NE", "NH1", "NH2"], ["OD1", "OD2"], ["NZ"], ["ND1", "NE2"], ["O", "OXT"], ["N"]


def load(path):
    atoms, het, biomt, res = [], [], {}, None
    for line in open(path):
        rec = line[:6]
        if rec in ("ATOM  ", "HETATM"):
            a = dict(rec=rec.strip(), name=line[12:16].strip(), alt=line[16].strip(), resn=line[17:20].strip(), ch=line[21],
                     num=int(line[22:26]), xyz=np.array([float(line[30:38]), float(line[38:46]), float(line[46:54])]),
                     occ=float(line[54:60] or 1), b=float(line[60:66] or 0))
            (atoms if rec == "ATOM  " else het).append(a)
        m = re.match(r"REMARK 350\s+BIOMT(\d)\s+(\d+)\s+(.*)", line)
        if m: biomt.setdefault(int(m.group(2)), {})[int(m.group(1))] = [float(x) for x in m.group(3).split()]
        m = re.match(r"REMARK   2 RESOLUTION\.\s+([\d.]+)", line)
        if m: res = float(m.group(1))
    return atoms, het, biomt, res


def apply(op, a):
    b = dict(a); b["xyz"] = op[0] @ a["xyz"] + op[1]; b["ch"] = a["ch"] + "'"; return b


def pick(atoms, ch, num, names):
    return [a for a in atoms if a["ch"] == ch and a["num"] == num and a["name"] in names]


def closest(A, B):
    best = None
    for p in A:
        for q in B:
            if p["alt"] and q["alt"] and p["alt"] != q["alt"] and p["ch"].rstrip("'") == q["ch"].rstrip("'"): continue
            d = float(np.linalg.norm(p["xyz"] - q["xyz"]))
            if best is None or d < best[0]: best = (d, p, q)
    return best


def tag(a):
    alt = f" alt {a['alt']}" if a["alt"] else ""
    occ = f", occ {a['occ']:.2f}" if a["occ"] < 1 else ""
    return f"{a['resn']}{a['num']}{a['ch']} {a['name']}{alt} (B {a['b']:.0f}{occ})"


def nearest_het(het, a, cut=3.6):
    hits = sorted(((float(np.linalg.norm(h["xyz"] - a["xyz"])), h) for h in het if h["resn"] not in ("HEM", "OXY")), key=lambda t: t[0])
    return [f"{h['resn']}{h['num']}{h['ch']} {h['name']} {d:.2f}" for d, h in hits if d <= cut][:3]


def report(kind, pid, folder):
    atoms, het, biomt, res = load(os.path.join(folder, pid + ".pdb"))
    print(f"\n== {pid} ({'T, deoxy' if kind == 'T' else 'R, oxy'}; {res} Å)")
    if kind == "R":
        op = (np.array([biomt[2][i][:3] for i in (1, 2, 3)]), np.array([biomt[2][i][3] for i in (1, 2, 3)]))
        atoms = atoms + [apply(op, a) for a in atoms]
        het = het + [apply(op, h) for h in het]
        ifaces = [("A", "A'", "B", "B'")]           # alpha1, alpha2, beta1, beta2
    else:
        ifaces = [("A", "C", "B", "D"), ("C", "A", "D", "B")]
    for a1, a2, b1, b2 in ifaces:
        print(f"  interface {a1}/{a2}:")
        rows = [
            ("Arg141α carboxylate – Val1α′ amine", pick(atoms, a1, 141, CTERM_O), pick(atoms, a2, 1, NTERM_N)),
            ("Arg141α carboxylate – Lys127α′ NZ", pick(atoms, a1, 141, CTERM_O), pick(atoms, a2, 127, LYS_N)),
            ("Arg141α guanidinium – Asp126α′", pick(atoms, a1, 141, ARG_N), pick(atoms, a2, 126, ASP_O)),
            ("His146β imidazole – Asp94β", pick(atoms, b1, 146, HIS_N), pick(atoms, b1, 94, ASP_O)),
            ("His146β carboxylate – Lys40α′", pick(atoms, b1, 146, CTERM_O), pick(atoms, a2, 40, LYS_N)),
        ]
        for name, A, B in rows:
            c = closest(A, B)
            if c is None: print(f"    {name:38s} atoms missing"); continue
            d, p, q = c
            flag = "" if d <= 4.0 else "  > 4 Å"
            print(f"    {name:36s} {d:5.2f} Å  {tag(p)} … {tag(q)}{flag}")
        cterm = pick(atoms, a1, 141, CTERM_O); nterm = pick(atoms, a2, 1, NTERM_N)
        for lab, grp in (("Arg141α C-terminus", cterm), ("Val1α′ N-terminus", nterm)):
            near = sorted({s for a in grp for s in nearest_het(het, a)})
            print(f"    waters/ions ≤ 3.6 Å from {lab}: {', '.join(near) if near else 'none'}")


if __name__ == "__main__":
    folder = sys.argv[1]
    for spec in sys.argv[2:]:
        kind, pid = spec.split(":")
        report(kind, pid, folder)
