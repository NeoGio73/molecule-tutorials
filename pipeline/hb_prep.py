#!/usr/bin/env python3
"""
hb_prep.py — hemoglobin T/R/sickle preprocessor for the Unity tutorial scene.

Reads three PDB files:
  T  : deoxyhemoglobin, tetramer          (default 2HHB)
  R  : oxyhemoglobin                       (default 1HHO; dimer in the ASU, the
                                            tetramer is rebuilt from REMARK 350)
  S  : deoxy sickle hemoglobin double strand (default 2HBS, two tetramers)

Writes one JSON file that Unity's JsonUtility can read directly (no
dictionaries, no nested arrays; coordinates are flat float arrays), plus a
plain-text report of the numbers worth checking against Lehninger.

Usage:
  python hb_prep.py --t 2HHB.pdb --r 1HHO.pdb --s 2HBS.pdb --out hemoglobin.json
  python hb_prep.py --t 2HHB.pdb --r 1HHO.pdb --out hemoglobin.json   # no sickle

Coordinates are in angstrom, centred on the T tetramer. Unity should scale by
0.1 and negate z on import (PDB is right-handed, Unity is left-handed).
"""

import argparse
import json
import math
import sys
import warnings
from collections import defaultdict
from datetime import date

import numpy as np

warnings.filterwarnings("ignore")
from Bio.PDB import PDBParser  # noqa: E402

# --------------------------------------------------------------------------
# Residue bookkeeping (Lehninger numbering; helix labels for display)
# --------------------------------------------------------------------------

NAMED_RESIDUES = [
    # role, resnum, resname, tag, helix position; the label is built as "α1 His⁸⁷ (F8)" (Lehninger superscripts)
    ("alpha", 87, "HIS", "proximal_his", "F8"),
    ("beta", 92, "HIS", "proximal_his", "F8"),
    ("alpha", 58, "HIS", "distal_his", "E7"),
    ("beta", 63, "HIS", "distal_his", "E7"),
    ("beta", 146, "HIS", "ionpair", "HC3"),
    ("beta", 94, "ASP", "ionpair", "FG1"),
    ("alpha", 40, "LYS", "ionpair", "C5"),
    ("alpha", 141, "ARG", "ionpair", "HC3"),
    ("alpha", 126, "ASP", "ionpair", "H9"),
    ("alpha", 1, "VAL", "ionpair", "NA1"),
    ("alpha", 127, "LYS", "ionpair", "H10"),
]
SUPERSCRIPT = str.maketrans("0123456789", "⁰¹²³⁴⁵⁶⁷⁸⁹")

# Lehninger Fig. 5-9 ion pairs, written by subunit role. Each entry is tried
# at the primary interface and at its symmetric mate (swap 1<->2).
ION_PAIRS = [
    ("His146β–Asp94β (intrachain)", "beta1", 146, ["ND1", "NE2"], "beta1", 94, ["OD1", "OD2"]),
    ("His146β carboxylate–Lys40α", "beta1", 146, ["O", "OXT"], "alpha2", 40, ["NZ"]),
    ("Arg141α guanidinium–Asp126α′", "alpha1", 141, ["NE", "NH1", "NH2"], "alpha2", 126, ["OD1", "OD2"]),
    ("Arg141α carboxylate–Val1α′ amine", "alpha1", 141, ["O", "OXT"], "alpha2", 1, ["N"]),
    # Perutz's list has the carboxylate on Lys127α′ instead; 2HHB supports that (2.6–2.8 Å).
    ("Arg141α carboxylate–Lys127α′", "alpha1", 141, ["O", "OXT"], "alpha2", 127, ["NZ"]),
]

# Measured and reported, but not drawn in the scene (decided Nov 7, 2026):
#  - Val1α′: named in the narration as in Lehninger Fig. 5-9, but in 2HHB/1HHO the contact does not break
#    (4.8 Å T -> 4.2 Å R), so a "breaking" line would contradict the narration.
#  - Lys127α′: breaks in the structures (2.8 -> 4.5 Å) but is not in Lehninger's figure; documented in the paper only.
HIDDEN_PAIRS = {"Arg141α carboxylate–Val1α′ amine", "Arg141α carboxylate–Lys127α′"}

CHAIN_LABEL = {"alpha1": "α1", "beta1": "β1", "alpha2": "α2", "beta2": "β2"}
HEME_PLANE_N = ["NA", "NB", "NC", "ND"]
BOND_CUTOFF = 1.95  # Å, covalent heavy-atom bond
FE_N_CUTOFF = 2.6   # Å, Fe–N coordination


# --------------------------------------------------------------------------
# Parsing
# --------------------------------------------------------------------------

def r3(v):
    return [round(float(x), 3) for x in v]


def parse_biomt(path):
    """Return list of 3x4 transforms for BIOMOLECULE 1 and the chains they apply to."""
    transforms = {}
    chains = []
    in_bio1 = False
    with open(path) as fh:
        for line in fh:
            if not line.startswith("REMARK 350"):
                continue
            if "BIOMOLECULE:" in line:
                in_bio1 = line.split("BIOMOLECULE:")[1].strip() == "1"
                continue
            if not in_bio1:
                continue
            if "APPLY THE FOLLOWING TO CHAINS:" in line or "AND CHAINS:" in line:
                part = line.split("CHAINS:")[1]
                chains += [c.strip() for c in part.split(",") if c.strip()]
            elif "BIOMT" in line:
                tok = line.split()
                row = int(tok[2][-1]) - 1
                idx = int(tok[3])
                vals = [float(x) for x in tok[4:8]]
                transforms.setdefault(idx, np.zeros((3, 4)))[row] = vals
    return [transforms[k] for k in sorted(transforms)], chains


def load_structure(path):
    """Return {chain_id: [residue dicts]} with residues in file order."""
    parser = PDBParser(QUIET=True)
    s = parser.get_structure("x", path)
    model = next(iter(s))
    out = {}
    for chain in model:
        residues = []
        for res in chain:
            hetflag, resnum, icode = res.id
            atoms = []
            for atom in res:
                if atom.is_disordered():
                    atom = atom.disordered_get("A") if atom.disordered_has_id("A") else atom.disordered_get()
                elem = (atom.element or atom.get_name()[0]).upper()
                if elem == "H":
                    continue
                atoms.append({"name": atom.get_name(), "elem": elem, "xyz": np.array(atom.coord, dtype=float)})
            residues.append({
                "resnum": resnum, "icode": icode.strip(), "resname": res.get_resname().strip(),
                "het": hetflag.strip() != "", "atoms": atoms,
            })
        out[chain.id] = residues
    return out


def apply_biomt(struct, transforms, apply_chains):
    """Generate the biological assembly. New chains get the next free IDs."""
    if len(transforms) <= 1:
        return struct
    used = set(struct.keys())
    free = [c for c in "ABCDEFGHIJKLMNOPQRSTUVWXYZ" if c not in used]
    out = dict(struct)
    for t in transforms[1:]:
        R, v = t[:, :3], t[:, 3]
        if np.allclose(R, np.eye(3)) and np.allclose(v, 0):
            continue
        for cid in apply_chains:
            if cid not in struct:
                continue
            new_id = free.pop(0)
            new_res = []
            for res in struct[cid]:
                new_atoms = [{"name": a["name"], "elem": a["elem"], "xyz": R @ a["xyz"] + v} for a in res["atoms"]]
                new_res.append({**res, "atoms": new_atoms})
            out[new_id] = new_res
    return out


# --------------------------------------------------------------------------
# Chain classification and tetramer assembly
# --------------------------------------------------------------------------

def protein_residues(residues):
    return [r for r in residues if not r["het"] and any(a["name"] == "CA" for a in r["atoms"])]


def ca_of(res):
    for a in res["atoms"]:
        if a["name"] == "CA":
            return a["xyz"]
    return None


def classify_chain(residues):
    prot = protein_residues(residues)
    n = len(prot)
    if n == 0:
        return None
    names = {r["resnum"]: r["resname"] for r in prot}
    if 135 <= n <= 143 or names.get(2) == "LEU":
        return "alpha"
    if 140 <= n <= 148 or names.get(2) == "HIS":
        return "beta"
    return None


def ca_contacts(res_a, res_b, cutoff=8.0):
    A = np.array([ca_of(r) for r in protein_residues(res_a)])
    B = np.array([ca_of(r) for r in protein_residues(res_b)])
    d = np.linalg.norm(A[:, None, :] - B[None, :, :], axis=2)
    return int((d < cutoff).sum())


def build_tetramers(struct, want=1):
    """Group chains into tetramers [{'alpha1':cid,'beta1':cid,'alpha2':cid,'beta2':cid}]."""
    kinds = {cid: classify_chain(res) for cid, res in struct.items()}
    alphas = [c for c, k in kinds.items() if k == "alpha"]
    betas = [c for c, k in kinds.items() if k == "beta"]
    if not alphas or not betas:
        sys.exit("Could not find alpha/beta chains in structure")
    # dimers: each alpha pairs with the beta it touches most (alpha1-beta1 interface)
    dimers = []
    free_betas = set(betas)
    for a in alphas:
        best = max(free_betas, key=lambda b: ca_contacts(struct[a], struct[b]))
        free_betas.discard(best)
        dimers.append((a, best))
    # tetramers: pair dimers by cross contacts (alpha of one vs beta of the other)
    def cross(d1, d2):
        return ca_contacts(struct[d1[0]], struct[d2[1]]) + ca_contacts(struct[d2[0]], struct[d1[1]])
    tetramers = []
    pool = list(dimers)
    while len(pool) >= 2:
        d1 = pool.pop(0)
        d2 = max(pool, key=lambda d: cross(d1, d))
        pool.remove(d2)
        tetramers.append({"alpha1": d1[0], "beta1": d1[1], "alpha2": d2[0], "beta2": d2[1]})
    if len(tetramers) < want:
        sys.exit(f"Needed {want} tetramer(s), found {len(tetramers)}; chains: {kinds}")
    return tetramers, kinds


# --------------------------------------------------------------------------
# Geometry
# --------------------------------------------------------------------------

def kabsch(P, Q):
    """Return R, t such that R @ p + t ≈ q (least squares over paired rows)."""
    pc, qc = P.mean(0), Q.mean(0)
    H = (P - pc).T @ (Q - qc)
    U, S, Vt = np.linalg.svd(H)
    d = np.sign(np.linalg.det(Vt.T @ U.T))
    D = np.diag([1, 1, d])
    R = Vt.T @ D @ U.T
    t = qc - R @ pc
    return R, t


def transform_struct(struct, R, t):
    out = {}
    for cid, residues in struct.items():
        out[cid] = [{**res, "atoms": [{**a, "xyz": R @ a["xyz"] + t} for a in res["atoms"]]} for res in residues]
    return out


def rotation_angle_deg(R):
    c = (np.trace(R) - 1.0) / 2.0
    return math.degrees(math.acos(max(-1.0, min(1.0, c))))


def residue_map(residues):
    return {(r["resnum"], r["icode"]): r for r in residues}


def common_ca(res_t, res_r):
    mt, mr = residue_map(protein_residues(res_t)), residue_map(protein_residues(res_r))
    keys = [k for k in mt if k in mr]
    T = np.array([ca_of(mt[k]) for k in keys])
    Rr = np.array([ca_of(mr[k]) for k in keys])
    return keys, T, Rr


def intra_bonds(atoms):
    bonds = []
    for i in range(len(atoms)):
        for j in range(i + 1, len(atoms)):
            d = np.linalg.norm(atoms[i]["xyz"] - atoms[j]["xyz"])
            cut = FE_N_CUTOFF if "FE" in (atoms[i]["elem"], atoms[j]["elem"]) else BOND_CUTOFF
            if d < cut:
                bonds += [i, j]
    return bonds


def plane_offset(atoms_by_name, ref_name=None):
    """Signed distance of FE from the plane of the four pyrrole N. Positive = toward ref atom."""
    pts = np.array([atoms_by_name[n] for n in HEME_PLANE_N])
    c = pts.mean(0)
    _, _, Vt = np.linalg.svd(pts - c)
    normal = Vt[2]
    fe = atoms_by_name["FE"]
    d = float(np.dot(fe - c, normal))
    if ref_name is not None and ref_name in atoms_by_name:
        if np.dot(atoms_by_name[ref_name] - c, normal) < 0:
            d = -d
    return d


def find_res(struct, cid, resnum, resname=None, het=None):
    for r in struct[cid]:
        if r["resnum"] == resnum and (resname is None or r["resname"] == resname) and (het is None or r["het"] == het):
            return r
    return None


def find_het(struct, cid, names):
    return [r for r in struct[cid] if r["het"] and r["resname"] in names]


# --------------------------------------------------------------------------
# Main build
# --------------------------------------------------------------------------

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--t", required=True, help="T-state PDB (deoxy, e.g. 2HHB.pdb)")
    ap.add_argument("--r", required=True, help="R-state PDB (oxy, e.g. 1HHO.pdb)")
    ap.add_argument("--s", default=None, help="Sickle PDB (e.g. 2HBS.pdb), optional")
    ap.add_argument("--out", default="hemoglobin.json")
    ap.add_argument("--report", default="hemoglobin_report.txt")
    args = ap.parse_args()

    notes = []
    rep = []

    # ---- load and assemble ------------------------------------------------
    T = load_structure(args.t)
    Rs = load_structure(args.r)
    bt, bt_chains = parse_biomt(args.r)
    if len(bt) > 1:
        Rs = apply_biomt(Rs, bt, bt_chains)
        notes.append(f"R structure assembled from REMARK 350 ({len(bt)} transforms)")
    T_tet, T_kinds = build_tetramers(T)
    R_tet, R_kinds = build_tetramers(Rs)
    T_tet, R_tet = T_tet[0], R_tet[0]
    rep.append(f"T chains by role: {T_tet}")
    rep.append(f"R chains by role: {R_tet}")

    # ---- superpose R on T by alpha1-beta1 CA ------------------------------
    P, Q = [], []
    for role in ("alpha1", "beta1"):
        _, ct, cr = common_ca(T[T_tet[role]], Rs[R_tet[role]])
        P.append(cr)
        Q.append(ct)
    P, Q = np.vstack(P), np.vstack(Q)
    Rmat, tvec = kabsch(P, Q)
    Rs = transform_struct(Rs, Rmat, tvec)
    rmsd11 = float(np.sqrt(((P @ Rmat.T + tvec - Q) ** 2).sum(1).mean()))
    rep.append(f"RMSD of alpha1beta1 CA after superposition: {rmsd11:.2f} Å")

    # rotation of alpha2beta2 between T and R (both now in T frame)
    P2, Q2 = [], []
    for role in ("alpha2", "beta2"):
        _, ct, cr = common_ca(T[T_tet[role]], Rs[R_tet[role]])
        P2.append(ct)
        Q2.append(cr)
    P2, Q2 = np.vstack(P2), np.vstack(Q2)
    R22, _ = kabsch(P2, Q2)
    rot22 = rotation_angle_deg(R22)
    rep.append(f"Rotation of alpha2beta2 relative to alpha1beta1, T->R: {rot22:.1f}° (Lehninger: ~15°)")

    # ---- centre on T tetramer ------------------------------------------------
    allca = np.array([ca_of(r) for role in T_tet for r in protein_residues(T[T_tet[role]])])
    centre = allca.mean(0)
    T = transform_struct(T, np.eye(3), -centre)
    Rs = transform_struct(Rs, np.eye(3), -centre)

    # ---- chains ---------------------------------------------------------
    chains_out = []
    for role in ("alpha1", "beta1", "alpha2", "beta2"):
        ct_id, cr_id = T_tet[role], R_tet[role]
        keys, ct, cr = common_ca(T[ct_id], Rs[cr_id])
        mt = residue_map(protein_residues(T[ct_id]))
        dropped = len(protein_residues(T[ct_id])) - len(keys)
        if dropped:
            notes.append(f"{role}: {dropped} residues not in both structures were dropped from the trace")
        chains_out.append({
            "id": ct_id, "idR": cr_id, "role": role, "label": CHAIN_LABEL[role],
            "kind": "alpha" if role.startswith("alpha") else "beta",
            "nres": len(keys),
            "resnum": [k[0] for k in keys],
            "resname": [mt[k]["resname"] for k in keys],
            "caT": r3(ct.flatten()), "caR": r3(cr.flatten()),
        })

    # ---- hemes and O2 -----------------------------------------------------
    hemes_out, ligands_out = [], []
    for role in ("alpha1", "beta1", "alpha2", "beta2"):
        ct_id, cr_id = T_tet[role], R_tet[role]
        hT = find_het(T, ct_id, {"HEM"})
        hR = find_het(Rs, cr_id, {"HEM"})
        if not hT or not hR:
            notes.append(f"{role}: heme missing in T or R")
            continue
        hT, hR = hT[0], hR[0]
        rmap = {a["name"]: a for a in hR["atoms"]}
        atoms = [a for a in hT["atoms"] if a["name"] in rmap]
        if len(atoms) != len(hT["atoms"]):
            notes.append(f"{role}: {len(hT['atoms']) - len(atoms)} heme atoms not shared between T and R")
        names = [a["name"] for a in atoms]
        byname_T = {a["name"]: a["xyz"] for a in atoms}
        byname_R = {a["name"]: rmap[a["name"]]["xyz"] for a in atoms}
        # proximal His NE2 as reference for the sign of the Fe offset
        prox_num = 87 if role.startswith("alpha") else 92
        hisT = find_res(T, ct_id, prox_num, "HIS", het=False)
        hisR = find_res(Rs, cr_id, prox_num, "HIS", het=False)
        ne2T = next((a["xyz"] for a in hisT["atoms"] if a["name"] == "NE2"), None) if hisT else None
        ne2R = next((a["xyz"] for a in hisR["atoms"] if a["name"] == "NE2"), None) if hisR else None
        offT = plane_offset({**byname_T, "REF": ne2T} if ne2T is not None else byname_T, "REF")
        offR = plane_offset({**byname_R, "REF": ne2R} if ne2R is not None else byname_R, "REF")
        feHisT = float(np.linalg.norm(byname_T["FE"] - ne2T)) if ne2T is not None else -1.0
        feHisR = float(np.linalg.norm(byname_R["FE"] - ne2R)) if ne2R is not None else -1.0
        hemes_out.append({
            "chain": ct_id, "role": role,
            "atoms": [{"name": a["name"], "elem": a["elem"], "T": r3(a["xyz"]), "R": r3(byname_R[a["name"]])} for a in atoms],
            "bonds": intra_bonds(atoms),
            "feIndex": names.index("FE"),
            "planeN": [names.index(n) for n in HEME_PLANE_N],
            "feOffPlaneT": round(offT, 3), "feOffPlaneR": round(offR, 3),
            "feHisDistT": round(feHisT, 3), "feHisDistR": round(feHisR, 3),
        })
        rep.append(f"{role} heme: Fe out of plane T {offT:+.2f} Å, R {offR:+.2f} Å (toward His F8 = +); "
                   f"Fe–NE2(His F8) T {feHisT:.2f} Å, R {feHisR:.2f} Å")
        # O2 (OXY) in R, bonded to Fe
        for lig in find_het(Rs, cr_id, {"OXY", "O2"}):
            fe = byname_R["FE"]
            if min(np.linalg.norm(a["xyz"] - fe) for a in lig["atoms"]) < 3.2:
                ligands_out.append({
                    "chain": ct_id, "role": role, "name": "O2",
                    "atoms": [{"name": a["name"], "elem": a["elem"], "T": [], "R": r3(a["xyz"])} for a in lig["atoms"]],
                    "bonds": intra_bonds(lig["atoms"]),
                })
                break
        else:
            notes.append(f"{role}: no O2 found bonded to Fe in R structure")

    # ---- named residues ----------------------------------------------------
    residues_out = []
    res_index = {}
    for kind, resnum, resname, tag, label in NAMED_RESIDUES:
        for role in (f"{kind}1", f"{kind}2"):
            ct_id, cr_id = T_tet[role], R_tet[role]
            rt = find_res(T, ct_id, resnum, None, het=False)
            rr = find_res(Rs, cr_id, resnum, None, het=False)
            if rt is None or rr is None:
                notes.append(f"{role} {resname}{resnum}: missing in T or R")
                continue
            if rt["resname"] != resname:
                notes.append(f"{role} residue {resnum} is {rt['resname']}, expected {resname}")
            rmap = {a["name"]: a for a in rr["atoms"]}
            atoms = [a for a in rt["atoms"] if a["name"] in rmap]
            missing = [a["name"] for a in rt["atoms"] if a["name"] not in rmap]
            if missing:
                notes.append(f"{role} {resname}{resnum}: atoms {missing} missing in R, dropped")
            key = f"{role}:{resnum}"
            res_index[key] = (rt, rr)
            lab = f"{CHAIN_LABEL[role]} {resname.title()}{str(resnum).translate(SUPERSCRIPT)} ({label})"
            residues_out.append({
                "key": key, "chain": ct_id, "role": role, "resname": rt["resname"], "resnum": resnum,
                "label": lab, "tag": tag,
                "atoms": [{"name": a["name"], "elem": a["elem"], "T": r3(a["xyz"]), "R": r3(rmap[a["name"]]["xyz"])} for a in atoms],
                "bonds": intra_bonds(atoms),
            })

    # ---- ion pairs --------------------------------------------------------
    def swap(role):
        return role[:-1] + ("2" if role.endswith("1") else "1")

    ion_out = []
    for name, r1, n1, a1s, r2, n2, a2s in ION_PAIRS:
        for interface, (ra, rb) in (("primary", (r1, r2)), ("symmetric", (swap(r1), swap(r2)))):
            ka, kb = f"{ra}:{n1}", f"{rb}:{n2}"
            if ka not in res_index or kb not in res_index:
                notes.append(f"ion pair {name} ({interface}): residue missing")
                continue
            (aT, aR), (bT, bR) = res_index[ka], res_index[kb]
            best = None
            for an in a1s:
                for bn in a2s:
                    pa = next((x["xyz"] for x in aT["atoms"] if x["name"] == an), None)
                    pb = next((x["xyz"] for x in bT["atoms"] if x["name"] == bn), None)
                    if pa is None or pb is None:
                        continue
                    d = float(np.linalg.norm(pa - pb))
                    if best is None or d < best[0]:
                        best = (d, an, bn)
            if best is None:
                notes.append(f"ion pair {name} ({interface}): candidate atoms missing (looked for {a1s} / {a2s})")
                continue
            dT, an, bn = best
            pa = next((x["xyz"] for x in aR["atoms"] if x["name"] == an), None)
            pb = next((x["xyz"] for x in bR["atoms"] if x["name"] == bn), None)
            dR = float(np.linalg.norm(pa - pb)) if pa is not None and pb is not None else -1.0
            ion_out.append({
                "name": name, "side": interface,
                "fromKey": ka, "fromAtom": an, "toKey": kb, "toAtom": bn,
                "distT": round(dT, 2), "distR": round(dR, 2), "hidden": name in HIDDEN_PAIRS,
            })
            flag = "" if dT < 4.0 else "   <-- longer than a salt bridge; check this residue"
            if name in HIDDEN_PAIRS: flag += "   [not drawn]"
            rep.append(f"ion pair {name:38s} {interface:9s} {an}…{bn}  T {dT:.2f} Å  R {dR:.2f} Å{flag}")

    # ---- sickle ------------------------------------------------------------
    sickle_out = {"source": "", "tetramers": [], "val6": [], "pocket": [], "contacts": []}
    if args.s:
        S = load_structure(args.s)
        S_tets, S_kinds = build_tetramers(S, want=2)
        # superpose the first sickle tetramer onto T (alpha1beta1 CA) so it sits in the scene frame
        P, Q = [], []
        for role in ("alpha1", "beta1"):
            _, ct, cs = common_ca(T[T_tet[role]], S[S_tets[0][role]])
            P.append(cs)
            Q.append(ct)
        Rm, tv = kabsch(np.vstack(P), np.vstack(Q))
        S = transform_struct(S, Rm, tv)
        sickle_out["source"] = args.s.split("/")[-1].split("\\")[-1]
        rep.append(f"Sickle tetramers: {S_tets}")
        for ti, tet in enumerate(S_tets):
            chains = []
            for role in ("alpha1", "beta1", "alpha2", "beta2"):
                prot = protein_residues(S[tet[role]])
                chains.append({
                    "id": tet[role], "role": role, "label": CHAIN_LABEL[role],
                    "kind": "alpha" if role.startswith("alpha") else "beta",
                    "resnum": [r["resnum"] for r in prot],
                    "ca": r3(np.array([ca_of(r) for r in prot]).flatten()),
                })
                if role.startswith("beta"):
                    v6 = find_res(S, tet[role], 6, None, het=False)
                    if v6 is not None:
                        if v6["resname"] != "VAL":
                            notes.append(f"sickle {tet[role]} residue 6 is {v6['resname']}, expected VAL")
                        sickle_out["val6"].append({
                            "tetramer": ti, "chain": tet[role], "role": role, "resname": v6["resname"], "resnum": 6,
                            "atoms": [{"name": a["name"], "elem": a["elem"], "T": r3(a["xyz"]), "R": []} for a in v6["atoms"]],
                            "bonds": intra_bonds(v6["atoms"]),
                        })
                    for pn, pname in ((85, "PHE"), (88, "LEU")):
                        pr = find_res(S, tet[role], pn, None, het=False)
                        if pr is not None:
                            sickle_out["pocket"].append({
                                "tetramer": ti, "chain": tet[role], "role": role, "resname": pr["resname"], "resnum": pn,
                                "atoms": [{"name": a["name"], "elem": a["elem"], "T": r3(a["xyz"]), "R": []} for a in pr["atoms"]],
                                "bonds": intra_bonds(pr["atoms"]),
                            })
            sickle_out["tetramers"].append({"index": ti, "chains": chains})
        # Val6 -> pocket contacts across tetramers
        for v in sickle_out["val6"]:
            vxyz = np.array([a["T"] for a in v["atoms"] if a["name"] in ("CB", "CG1", "CG2")])
            for p in sickle_out["pocket"]:
                if p["tetramer"] == v["tetramer"]:
                    continue
                pxyz = np.array([a["T"] for a in p["atoms"]])
                d = float(np.linalg.norm(vxyz[:, None, :] - pxyz[None, :, :], axis=2).min())
                if d < 5.0:
                    sickle_out["contacts"].append({
                        "val6Tetramer": v["tetramer"], "val6Chain": v["chain"],
                        "pocketTetramer": p["tetramer"], "pocketChain": p["chain"],
                        "pocketResname": p["resname"], "pocketResnum": p["resnum"], "minDist": round(d, 2),
                    })
                    rep.append(f"sickle contact: Val6 of {v['chain']} (tet {v['tetramer']}) -> "
                               f"{p['resname']}{p['resnum']} of {p['chain']} (tet {p['tetramer']}), {d:.2f} Å")
        if not sickle_out["contacts"]:
            notes.append("sickle: no Val6 -> Phe85/Leu88 contact under 5 Å between the two tetramers; "
                         "check which tetramers the file contains")

    # ---- write ------------------------------------------------------------
    out = {
        "meta": {
            "units": "angstrom", "unityScale": 0.1, "mirrorZ": True,
            "sourceT": args.t.split("/")[-1].split("\\")[-1],
            "sourceR": args.r.split("/")[-1].split("\\")[-1],
            "sourceS": sickle_out["source"],
            "generated": date.today().isoformat(),
            "notes": notes,
        },
        "chains": chains_out,
        "hemes": hemes_out,
        "ligands": ligands_out,
        "residues": residues_out,
        "ionPairs": ion_out,
        "metrics": {"rmsdAlpha1Beta1": round(rmsd11, 3), "rotationAlpha2Beta2Deg": round(rot22, 2)},
        "sickle": sickle_out,
    }
    with open(args.out, "w") as fh:
        json.dump(out, fh, separators=(",", ":"))
    with open(args.report, "w", encoding="utf-8") as fh:
        fh.write("hb_prep report\n==============\n")
        fh.write("\n".join(rep) + "\n")
        if notes:
            fh.write("\nNotes\n-----\n" + "\n".join(notes) + "\n")
    print("\n".join(rep))
    if notes:
        print("\nNotes:\n  " + "\n  ".join(notes))
    print(f"\nWrote {args.out} and {args.report}")


if __name__ == "__main__":
    main()
