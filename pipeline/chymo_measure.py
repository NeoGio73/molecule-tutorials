"""Chymotrypsin: measure the structures behind the tutorial's claims, the way hb_prep.py checked hemoglobin.
Structures (chymotrypsinogen numbering throughout):
  1YPH  alpha-chymotrypsin, 1.34 A (M-CSA reference): the free enzyme, the triad, the oxyanion hole
  1CHO  alpha-chymotrypsin with turkey ovomucoid third domain (OMTKY3): a Michaelis-like complex, P1 = Leu18 of chain I
  7GCH  gamma-chymotrypsin with Ac-Leu-Phe-CF3: Ser195 hemiketal, a tetrahedral-intermediate mimic
  2CGA  chymotrypsinogen A: the zymogen
Every structure is superposed on 1YPH by core C-alpha atoms, so positions can be compared across them.
Usage: python3 chymo_measure.py <folder with the four .pdb files> [report.txt] [numbers.json]"""
import json, math, os, sys, warnings
import numpy as np
from Bio.PDB import PDBParser
from Bio.PDB.PDBExceptions import PDBConstructionWarning

warnings.simplefilter("ignore", PDBConstructionWarning)
AA = {"ALA", "ARG", "ASN", "ASP", "CYS", "GLN", "GLU", "GLY", "HIS", "ILE", "LEU", "LYS", "MET", "PHE", "PRO",
      "SER", "THR", "TRP", "TYR", "VAL"}
RING = ("CG", "CD1", "CD2", "CE1", "CE2", "CZ")
out = []
def say(s=""): out.append(s); print(s)


def load(folder, pid):
    for name in (f"{pid}.pdb", f"{pid.lower()}.pdb", f"pdb{pid.lower()}.ent"):
        p = os.path.join(folder, name)
        if os.path.exists(p):
            return PDBParser(QUIET=True).get_structure(pid, p)[0]
    raise FileNotFoundError(pid)


def kabsch(P, Q):
    pc, qc = P.mean(0), Q.mean(0)
    U, S, Vt = np.linalg.svd((P - pc).T @ (Q - qc))
    D = np.diag([1, 1, np.sign(np.linalg.det(Vt.T @ U.T))])
    R = Vt.T @ D @ U.T
    return R, qc - R @ pc


def residues(model):
    """All residues, protein or not, as (chain id, residue) pairs."""
    return [(ch.id, r) for ch in model for r in ch]


def enzyme_copy(model):
    """Pick one enzyme molecule: the His57/Asp102/Ser195 triad whose atoms sit together. Returns {resnum: residue}
    for the protein residues of the chains that make up that molecule (chymotrypsinogen numbering is unique per copy)."""
    by = {}
    for cid, r in residues(model):
        if r.id[0] == " " and r.get_resname() in AA:
            by.setdefault((r.id[1], r.get_resname()), []).append((cid, r))
    best = None
    for c1, his in by.get((57, "HIS"), []):
        for c2, ser in by.get((195, "SER"), []):
            for c3, asp in by.get((102, "ASP"), []):
                if "NE2" in his and "OG" in ser and "ND1" in his and "OD1" in asp:
                    dd = d(his["NE2"], ser["OG"]) + min(d(his["ND1"], asp["OD1"]), d(his["ND1"], asp["OD2"]))
                    if best is None or dd < best[0]:
                        best = (dd, {c1, c2, c3})
    chains = set(best[1])
    # Add the rest of the same molecule by its disulfides: alpha- and gamma-chymotrypsin are three chains (1-13, 16-146,
    # 149-245) held together by Cys1-Cys122 and Cys136-Cys201. Following S-S bonds never picks up a neighbour in the
    # crystal or a bound inhibitor, which a distance cutoff did.
    sg = {ch.id: [r["SG"].coord for r in ch if r.get_resname() == "CYS" and "SG" in r] for ch in model}
    grew = True
    while grew:
        grew = False
        for cid in list(sg):
            if cid in chains or not sg[cid]: continue
            if any(np.linalg.norm(x - y) < 2.3 for c in chains for x in sg[c] for y in sg[cid]):
                chains.add(cid); grew = True
    m = {}
    for cid, r in residues(model):
        if cid in chains and r.id[0] == " " and r.get_resname() in AA and r.id[2] == " ":
            m.setdefault(r.id[1], r)
    return m, sorted(chains)


def d(a, b): return float(np.linalg.norm(a.coord - b.coord)) if hasattr(a, "coord") else float(np.linalg.norm(a - b))


def angle(a, b, c):
    a, b, c = [x.coord if hasattr(x, "coord") else x for x in (a, b, c)]
    u, v = a - b, c - b
    return math.degrees(math.acos(np.clip(u @ v / np.linalg.norm(u) / np.linalg.norm(v), -1, 1)))


def amide_h(m, n):
    """Backbone amide H of residue n, placed from N, C-alpha and the previous residue's C (1.01 A, in plane)."""
    r, p = m.get(n), m.get(n - 1)
    if r is None or p is None or "C" not in p: return None
    N, CA, C = r["N"].coord, r["CA"].coord, p["C"].coord
    u = (N - C) / np.linalg.norm(N - C) + (N - CA) / np.linalg.norm(N - CA)
    return N + 1.01 * u / np.linalg.norm(u)


def superpose(m, ref):
    """Fit structure m onto ref by C-alpha atoms of shared residue numbers, trimming >2 A outliers twice.
    Returns R, t, rmsd, n used."""
    common = sorted(set(m) & set(ref))
    P = np.array([m[k]["CA"].coord for k in common]); Q = np.array([ref[k]["CA"].coord for k in common])
    keep = np.ones(len(common), bool)
    for _ in range(3):
        R, t = kabsch(P[keep], Q[keep])
        dev = np.linalg.norm(P @ R.T + t - Q, axis=1)
        keep = dev < 2.0
    R, t = kabsch(P[keep], Q[keep])
    dev = np.linalg.norm(P[keep] @ R.T + t - Q[keep], axis=1)
    return R, t, float(np.sqrt((dev ** 2).mean())), int(keep.sum())


def xf(v, R, t): return (v.coord if hasattr(v, "coord") else v) @ R.T + t


def triad(m, tag, N):
    his, asp, ser = m[57], m[102], m[195]
    dh = min(d(his["ND1"], asp["OD1"]), d(his["ND1"], asp["OD2"]))
    which = "OD1" if d(his["ND1"], asp["OD1"]) <= d(his["ND1"], asp["OD2"]) else "OD2"
    ds = d(his["NE2"], ser["OG"])
    say(f"  {tag}: Asp102 {which}...His57 ND1 {dh:.2f} A; His57 NE2...Ser195 OG {ds:.2f} A")
    N[tag] = {"asp102_his57": round(dh, 2), "his57_ser195": round(ds, 2)}
    return dh, ds


def hole(m, o, tag, N, label):
    """Distances from an oxygen (oxyanion or carbonyl O) to the backbone N-H groups near it."""
    rows = []
    for n in sorted(m):
        r = m[n]
        if "N" not in r or n in (195 - 0,) and False: continue
        dist = d(r["N"].coord, o)
        if dist < 3.6:
            h = amide_h(m, n)
            ang = angle(r["N"].coord, h, o) if h is not None else float("nan")
            rows.append((n, r.get_resname().title(), dist, ang))
    say(f"  {tag}: backbone N within 3.6 A of the {label}:")
    for n, nm, dist, ang in rows:
        say(f"     {nm}{n} N...O {dist:.2f} A, N-H...O angle {ang:.0f} deg")
    N.setdefault(tag, {})["oxyanion_hole"] = [{"res": f"{nm}{n}", "N_O": round(dist, 2), "NHO_deg": round(ang)} for n, nm, dist, ang in rows]
    return rows


def main(folder, rep_path, json_path):
    N = {}
    st = {k: load(folder, k) for k in ("1YPH", "1CHO", "7GCH", "2CGA")}
    M = {}
    for k, s in st.items():
        M[k], chains = enzyme_copy(s)
        say(f"{k}: enzyme chains {', '.join(chains)}; {len(M[k])} residues, numbered {min(M[k])}-{max(M[k])}")
    ref = M["1YPH"]
    say("")
    say("Superposition on 1YPH (C-alpha, core after trimming >2 A outliers):")
    T = {"1YPH": (np.eye(3), np.zeros(3))}
    for k in ("1CHO", "7GCH", "2CGA"):
        R, t, rmsd, n = superpose(M[k], ref)
        T[k] = (R, t)
        say(f"  {k}: {n} C-alpha, rmsd {rmsd:.2f} A")
        N.setdefault("superposition", {})[k] = {"n": n, "rmsd": round(rmsd, 2)}

    say("")
    say("1. Catalytic triad (Lehninger: substrate binding compresses the Asp102-His57 hydrogen bond into a low-barrier one)")
    for k in ("1YPH", "1CHO", "7GCH", "2CGA"):
        triad(M[k], k, N)

    # ---- 1CHO: the P1 residue of the inhibitor (Leu18 of OMTKY3) sits like a substrate
    say("")
    say("2. Michaelis-like complex, 1CHO (OMTKY3, P1 = Leu18 of chain I)")
    p1 = None
    for cid, r in residues(st["1CHO"]):
        if r.id[1] == 18 and r.get_resname() == "LEU" and r.id[0] == " " and cid not in ("E", "F", "G") :
            p1 = (cid, r)
    if p1 is None:   # fall back: the residue whose carbonyl C is closest to Ser195 OG and is not in the enzyme
        og = M["1CHO"][195]["OG"]
        enz = set(id(r) for r in M["1CHO"].values())
        cand = [(d(r["C"], og), cid, r) for cid, r in residues(st["1CHO"]) if "C" in r and id(r) not in enz and r.get_resname() in AA]
        _, cid, r = min(cand, key=lambda x: x[0]); p1 = (cid, r)
    cid, leu = p1
    og = M["1CHO"][195]["OG"]
    say(f"  P1 = {leu.get_resname().title()}{leu.id[1]} of chain {cid}")
    say(f"  Ser195 OG...P1 carbonyl C {d(og, leu['C']):.2f} A; OG...C=O angle {angle(og, leu['C'], leu['O']):.0f} deg (Burgi-Dunitz ~105)")
    N["1CHO"].update({"P1": f"{leu.get_resname().title()}{leu.id[1]}{cid}", "OG_C": round(d(og, leu['C']), 2),
                      "OG_C_O_deg": round(angle(og, leu['C'], leu['O']))})
    hole(M["1CHO"], leu["O"].coord, "1CHO", N, "P1 carbonyl O")

    # ---- 7GCH: the hemiketal
    say("")
    say("3. Tetrahedral-intermediate mimic, 7GCH (Ac-Leu-Phe-CF3 hemiketal on Ser195)")
    og = M["7GCH"][195]["OG"]
    enz = set(id(r) for r in M["7GCH"].values())
    lig_atoms = [(a, r) for cid, r in residues(st["7GCH"]) if id(r) not in enz and r.get_resname() != "HOH" for a in r]
    ck = min(lig_atoms, key=lambda ar: d(ar[0], og))
    ckat, ckres = ck
    say(f"  ligand atom bonded to Ser195 OG: {ckres.get_resname()} {ckres.id[1]} {ckat.get_id()} at {d(ckat, og):.2f} A (covalent if ~1.4-1.5)")
    oxy = [a for a, r in lig_atoms if a.element == "O" and a is not ckat and d(a, ckat) < 1.6]
    N["7GCH"].update({"ligand_C": f"{ckres.get_resname()}{ckres.id[1]}:{ckat.get_id()}", "OG_C": round(d(ckat, og), 2)})
    if oxy:
        o = oxy[0]
        say(f"  hemiketal O (the oxyanion mimic): {o.get_parent().get_resname()} {o.get_id()}, C-O {d(o, ckat):.2f} A")
        hole(M["7GCH"], o.coord, "7GCH", N, "hemiketal O")
        o_probe = xf(o, *T["7GCH"])                                         # in 1YPH frame
    else:
        say("  (no O bonded to that carbon found)"); o_probe = None
    # the P1 phenyl: LPF names its ring carbons CP1-CP6; a peptide-style ligand would use CG..CZ of a PHE
    ring_atoms = [a for a, r in lig_atoms if a.get_id() in ("CP1", "CP2", "CP3", "CP4", "CP5", "CP6")] or \
                 [a for a, r in lig_atoms if a.get_id() in RING and r.get_resname() == "PHE"]
    if len(ring_atoms) >= 6:
        cen = np.mean([a.coord for a in ring_atoms[:6]], axis=0)
        near = {}
        for n, r in M["7GCH"].items():
            for a in r:
                dist = min(np.linalg.norm(a.coord - b.coord) for b in ring_atoms[:6])
                if dist < 4.5:
                    near[n] = min(near.get(n, 9), dist)
        lst = ", ".join(f"{M['7GCH'][n].get_resname().title()}{n} {v:.1f}" for n, v in sorted(near.items()))
        say(f"  Phe ring of the ligand: enzyme residues within 4.5 A (closest atom): {lst}")
        N["7GCH"]["pocket_contacts"] = {f"{M['7GCH'][n].get_resname().title()}{n}": round(float(v), 1) for n, v in sorted(near.items())}
        polar = [n for n in near if M["7GCH"][n].get_resname() in ("ASP", "GLU", "LYS", "ARG")]
        say(f"  charged side chains among them: {', '.join(M['7GCH'][n].get_resname().title() + str(n) for n in polar) or 'none'}")

    # ---- the free enzyme: is the oxyanion hole already built?
    say("")
    say("4. Oxyanion hole in the free enzyme and in the zymogen (probe = the 7GCH hemiketal O, moved into each frame)")
    if o_probe is not None:
        for k in ("1YPH", "2CGA"):
            R, t = T[k]; Ri, ti = R.T, -R.T @ t                                # 1YPH frame -> structure k
            probe = o_probe @ Ri.T + ti
            for n in (193, 195):
                r = M[k].get(n)
                if r is None: continue
                h = amide_h(M[k], n)
                say(f"  {k}: {r.get_resname().title()}{n} N...probe {d(r['N'].coord, probe):.2f} A, N-H...probe angle "
                    f"{angle(r['N'].coord, h, probe):.0f} deg")
                N.setdefault(k, {}).setdefault("hole_probe", {})[f"{r.get_resname().title()}{n}"] = {
                    "N_probe": round(d(r['N'].coord, probe), 2), "NH_probe_deg": round(angle(r['N'].coord, h, probe))}

    # ---- activation: the new N-terminus Ile16 and Asp194
    say("")
    say("5. Activation (Lehninger: trypsin cuts Arg15-Ile16; the shape change forms the pocket and the oxyanion hole)")
    for k in ("1YPH", "2CGA"):
        m = M[k]
        if 16 in m and 194 in m:
            i16, a194 = m[16], m[194]
            dd = min(d(i16["N"], a194[x]) for x in ("OD1", "OD2"))
            say(f"  {k}: Ile16 N...Asp194 carboxylate {dd:.2f} A; residue 15 present: {15 in m}")
            N[k]["ile16_asp194"] = round(dd, 2)
    # how far the pocket and hole move between zymogen and enzyme
    R, t = T["2CGA"]
    for label, rng in (("oxyanion-hole loop 191-194", range(191, 195)), ("pocket segment 189-192", range(189, 193)),
                       ("pocket segment 214-220", range(214, 221)), ("N-terminal segment 16-19", range(16, 20))):
        dev = [np.linalg.norm(xf(M["2CGA"][n]["CA"], R, t) - M["1YPH"][n]["CA"].coord) for n in rng if n in M["2CGA"] and n in M["1YPH"]]
        if dev:
            say(f"  2CGA vs 1YPH, {label}: C-alpha shift mean {np.mean(dev):.1f} A, max {np.max(dev):.1f} A")
            N.setdefault("activation_shift", {})[label] = {"mean": round(float(np.mean(dev)), 1), "max": round(float(np.max(dev)), 1)}

    open(rep_path, "w").write("\n".join(out) + "\n")
    json.dump(N, open(json_path, "w"), indent=1)


if __name__ == "__main__":
    folder = sys.argv[1]
    main(folder, sys.argv[2] if len(sys.argv) > 2 else os.path.join(folder, "chymotrypsin_report.txt"),
         sys.argv[3] if len(sys.argv) > 3 else os.path.join(folder, "chymotrypsin_measurements.json"))
