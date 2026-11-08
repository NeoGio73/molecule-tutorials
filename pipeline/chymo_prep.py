"""Chymotrypsin preprocessor: writes chymotrypsin.json for the Unity scene.

Frame: 1YPH (alpha-chymotrypsin, 1.34 A). 1CHO, 7GCH and 2CGA are superposed on it by core C-alpha atoms
(chymo_measure.superpose), so every position below is in one frame.

What the scene gets:
  trace      C-alpha trace of 1YPH (three chains: 1-13, 16-146, 149-245).
  zymogen    for the coda: chymotrypsinogen (2CGA) residues 1-245, with each residue's alpha-chymotrypsin position
             (residues 14, 15, 147 and 148 have none; chymotrypsin cuts them out).
  context    sticks that never change: the hydrophobic-pocket residues and the Ile16-Asp194 ion pair (1YPH).
  pocketSurface  a translucent surface patch lining the hydrophobic pocket (smooth Gaussian surface of the 1YPH atoms,
             cut to the largest piece within 7 A of the Phe ring).
  atoms      every atom that moves, appears or changes bonds during the mechanism, with a position in each of the eight
             states of Lehninger Fig. 6-27:
               0 free enzyme  1 ES complex  2 short-lived intermediate (acylation)  3 acyl-enzyme, product 1 leaving
               4 acyl-enzyme with water  5 short-lived intermediate (deacylation)  6 enzyme-product 2 complex  7 free enzyme
  bonds      a bond order per state: 0 none, 1 single, 2 double, 0.5 hydrogen bond (drawn dashed).
  charges    formal charge per state (circled sign next to the atom, as in the panels).
  arrows     the curved arrows of each state, the same arrows as the 2D panels ch_p03-ch_p07: from an atom (a lone pair)
             or a bond, to an atom or a bond.

How the states are built (Methods says this):
  * Free enzyme: 1YPH. Tetrahedral intermediates and acyl-enzyme: 7GCH (gamma-chymotrypsin with Ac-Leu-Phe-CF3, whose
    hemiketal carbon is covalently bonded to Ser195 O-gamma). ES complex and enzyme-product 2 complex: Ser195 and the
    oxyanion-hole backbone from 1CHO (alpha-chymotrypsin with turkey ovomucoid third domain, a substrate-like inhibitor).
  * His57 and Asp102 take 7GCH positions in every bound state, so the Asp102-His57 hydrogen bond shortens when substrate
    binds (2.60 A in 1YPH, 2.49 A in 7GCH), the order Lehninger gives. In 1CHO it is 2.76 A; the paper reports this.
  * The substrate is the 7GCH ligand throughout. Its CF3 carbon marks where the leaving group's N sits in the
    tetrahedral intermediate; the N, its H and its C-alpha are placed on the C-F directions.
  * ES: the carbonyl carbon is flattened, then the substrate is fitted onto the 1CHO P1 backbone (Leu18 C-alpha, C, O
    and Glu19 N), with the Phe ring held where it sits in the pocket. 1CHO puts Ser195 O-gamma 2.95 A from that carbon.
  * Acyl-enzyme: the ester carbon flattened in the plane of O-gamma, O and C-alpha; the leaving group drifts out.
  * Water: placed about 3 A from the ester carbon and hydrogen-bonded to His57 N-epsilon2, on the side the leaving group
    left. The second intermediate reuses the first one's carbon geometry (Lehninger: deacylation mirrors acylation).
    Product 2 sits where the substrate sat in the ES complex, the water O in place of the leaving N.
  * Hydrogens that matter (the protons that move, the oxyanion-hole N-H, His57 N-delta1-H) are placed by geometry.
Usage: python3 chymo_prep.py <folder with 1YPH/1CHO/7GCH/2CGA .pdb> <out.json>"""
import json, sys
from datetime import date
import numpy as np
from scipy.spatial import cKDTree
from skimage.measure import marching_cubes
import chymo_measure as cm

STATES = ["free", "es", "ti1", "acyl", "water", "ti2", "ep2", "free2"]
NS = len(STATES)
SI = {s: i for i, s in enumerate(STATES)}


def unit(v): return v / np.linalg.norm(v)
def dist(a, b): return float(np.linalg.norm(np.asarray(a) - np.asarray(b)))


def plane_project(p, a, b, c):
    n = unit(np.cross(b - a, c - a)); return p - ((p - a) @ n) * n


def wkabsch(P, Q, w):
    """Weighted rigid fit of points P onto Q; returns R, t with P @ R.T + t ~ Q."""
    w = np.asarray(w, float) / np.sum(w)
    pc, qc = (P * w[:, None]).sum(0), (Q * w[:, None]).sum(0)
    H = ((P - pc) * w[:, None]).T @ (Q - qc)
    U, S, Vt = np.linalg.svd(H)
    D = np.diag([1, 1, np.sign(np.linalg.det(Vt.T @ U.T))])
    R = Vt.T @ D @ U.T
    return R, qc - R @ pc


def place(targets, start, iters=400):
    """A point at given distances from given atoms (least squares), starting from `start`."""
    p = np.array(start, float)
    for _ in range(iters):
        g = np.zeros(3)
        for q, d0, wt in targets:
            v = p - q; r = np.linalg.norm(v); g += wt * (r - d0) * v / r
        p -= 0.2 * g
    return p


def pocket_surface(ref, centre, half=11.0, h=0.5, k=1.6, keep_r=7.0):
    """Translucent surface for the hydrophobic pocket: a smooth (Gaussian) surface of the protein atoms around the
    pocket, cut to a patch within keep_r of the Phe ring (the largest connected piece, so no loose fragments).
    Returns vertex and triangle arrays in angstrom (marching-cubes winding: front faces point out of the protein)."""
    VDW = {"C": 1.7, "N": 1.55, "O": 1.52, "S": 1.8}
    near = [(a.element, a.coord) for n in ref for a in ref[n] if a.element != "H" and np.linalg.norm(a.coord - centre) < half + 3]
    ax = np.arange(-half, half + h / 2, h)
    G = np.stack(np.meshgrid(ax, ax, ax, indexing="ij"), -1) + centre
    dens = np.zeros(G.shape[:3])
    for e, c in near:
        r = VDW.get(e, 1.7)
        dens += np.exp(-k * (((G - c) ** 2).sum(-1) / r ** 2 - 1))
    verts, faces, _, _ = marching_cubes(dens, level=1.0, spacing=(h, h, h))
    verts = verts + centre - half
    faces = faces[np.linalg.norm(verts[faces].mean(1) - centre, axis=1) < keep_r]
    # largest connected piece (faces sharing vertices)
    parent = list(range(len(verts)))
    def find(x):
        while parent[x] != x: parent[x] = parent[parent[x]]; x = parent[x]
        return x
    for a, b, c in faces:
        ra, rb, rc = find(a), find(b), find(c); parent[rb] = ra; parent[find(c)] = ra
    roots = np.array([find(f[0]) for f in faces])
    vals, counts = np.unique(roots, return_counts=True)
    faces = faces[roots == vals[np.argmax(counts)]]
    used = np.unique(faces); remap = -np.ones(len(verts), int); remap[used] = np.arange(len(used))
    return verts[used], remap[faces]


def main(folder, out):
    st = {k: cm.load(folder, k) for k in ("1YPH", "1CHO", "7GCH", "2CGA")}
    M = {k: cm.enzyme_copy(s)[0] for k, s in st.items()}
    ref = M["1YPH"]
    T = {"1YPH": (np.eye(3), np.zeros(3))}
    fit = {}
    for k in ("1CHO", "7GCH", "2CGA"):
        R, t, rmsd, n = cm.superpose(M[k], ref); T[k] = (R, t); fit[k] = (round(rmsd, 2), n)
    X = lambda k, v: (v.coord if hasattr(v, "coord") else np.asarray(v)) @ T[k][0].T + T[k][1]
    r3 = lambda v: [round(float(x), 3) for x in v]

    # ---------------- enzyme trace (1YPH) as continuous segments
    nums = sorted(n for n in ref if "CA" in ref[n])
    segs, cur = [], [nums[0]]
    for a, b in zip(nums, nums[1:]):
        if b != a + 1: segs.append(cur); cur = []
        cur.append(b)
    segs.append(cur)
    trace = [{"resnum": s, "ca": [x for n in s for x in r3(ref[n]["CA"].coord)]} for s in segs]

    # ---------------- zymogen for the coda: 2CGA 1-245, with each residue's place in alpha-chymotrypsin
    Z = M["2CGA"]
    znums = sorted(n for n in Z if "CA" in Z[n])
    zym = {"resnum": znums,
           "ca": [x for n in znums for x in r3(X("2CGA", Z[n]["CA"]))],
           "caActive": [x for n in znums for x in r3(ref[n]["CA"].coord if n in ref else X("2CGA", Z[n]["CA"]))],
           "inActive": [1 if n in ref else 0 for n in znums]}

    # ---------------- the substrate (7GCH ligand) in the 1YPH frame
    lig = [a for ch in st["7GCH"] for r in ch if r.get_resname() == "LPF" for a in r]
    L = {a.get_id(): X("7GCH", a) for a in lig}
    ring = np.mean([L["CP" + str(i)] for i in range(1, 7)], axis=0)

    # ---------------- context sticks (1YPH): pocket residues within 5 A of the Phe ring, and the activation ion pair
    def stick_res(n, tag):
        r = ref[n]
        atoms_ = [{"name": a.get_id(), "elem": a.element, "P": r3(a.coord)} for a in r if a.element != "H"]
        bonds_ = []
        for i in range(len(atoms_)):
            for j in range(i + 1, len(atoms_)):
                if dist(atoms_[i]["P"], atoms_[j]["P"]) < 1.9: bonds_ += [i, j]
        return {"key": f"{tag}:{n}", "resname": r.get_resname().title(), "resnum": n, "tag": tag, "atoms": atoms_, "bonds": bonds_}
    ring_atoms = [L["CP" + str(i)] for i in range(1, 7)] + [L["C4"]]
    pocket = sorted(n for n in ref if n not in (16, 57, 102, 193, 194, 195)
                    and min(dist(a.coord, p) for a in ref[n] for p in ring_atoms) < 5.0)
    context = [stick_res(n, "pocket") for n in pocket] + [stick_res(n, "activation") for n in (16, 194)]
    sv, sf = pocket_surface(ref, ring)
    surface = {"v": [round(float(x), 2) for x in sv.reshape(-1)], "t": [int(x) for x in sf.reshape(-1)]}

    # ---------------- reacting atoms
    atoms, idx = [], {}
    P = {}                                                    # name -> [NS x 3]

    def add(name, elem, cls, res=""):
        idx[name] = len(atoms)
        atoms.append({"name": name, "elem": elem, "cls": cls, "res": res})
        P[name] = np.full((NS, 3), np.nan)

    def put(name, states, p):
        for s in ([states] if isinstance(states, str) else states):
            P[name][SI[s]] = p

    # enzyme residues; source structure per state
    ENZ = {"His57": (57, ["CA", "CB", "CG", "ND1", "CD2", "CE1", "NE2"]), "Asp102": (102, ["CA", "CB", "CG", "OD1", "OD2"]),
           "Ser195": (195, ["N", "CA", "CB", "OG"]), "Gly193": (193, ["N", "CA"])}
    src = {"His57": ["1YPH", "7GCH", "7GCH", "7GCH", "7GCH", "7GCH", "7GCH", "1YPH"],
           "Asp102": ["1YPH", "7GCH", "7GCH", "7GCH", "7GCH", "7GCH", "7GCH", "1YPH"],
           "Ser195": ["1YPH", "1CHO", "7GCH", "7GCH", "7GCH", "7GCH", "1CHO", "1YPH"],
           "Gly193": ["1YPH", "1CHO", "7GCH", "7GCH", "7GCH", "7GCH", "1CHO", "1YPH"]}
    for res, (n, names) in ENZ.items():
        for an in names:
            add(f"{res}:{an}", an[0], "enzyme", res)
            for s in range(NS):
                P[f"{res}:{an}"][s] = X(src[res][s], M[src[res][s]][n][an])
        if res in ("Gly193", "Ser195"):                       # backbone amide H of the oxyanion hole
            add(f"{res}:H", "H", "enzyme", res)
            for s in range(NS):
                P[f"{res}:H"][s] = X(src[res][s], cm.amide_h(M[src[res][s]], n))
    # the Asp102 O that takes His57's N-delta1-H
    asp_o = "Asp102:OD1" if dist(P["His57:ND1"][0], P["Asp102:OD1"][0]) < dist(P["His57:ND1"][0], P["Asp102:OD2"][0]) else "Asp102:OD2"
    add("His57:HD1", "H", "enzyme", "His57")
    for s in range(NS):
        nd = P["His57:ND1"][s]; P["His57:HD1"][s] = nd + 1.01 * unit(P[asp_o][s] - nd)
    E = lambda name, s: P[name][SI[s]]

    # substrate (acyl half), leaving group, moving protons, water
    ACYL = ["C2", "O2", "C3", "N3", "C4", "CP1", "CP2", "CP3", "CP4", "CP5", "CP6", "CN1", "ON1", "CN2", "NL1",
            "CL1", "OL1", "CL2", "CA1", "CA2", "CA3", "CA4"]
    LEAV = [("L:N", "N"), ("L:CA", "C"), ("L:CB", "C"), ("L:H", "H")]
    for an in ACYL: add("S:" + an, {"N": "N", "O": "O"}.get(an[0], "C"), "acyl", "substrate")
    for nm, el in LEAV: add(nm, el, "leaving", "substrate")
    add("H1", "H", "proton"); add("H2", "H", "proton")
    add("W:O", "O", "water", "water"); add("W:H", "H", "water", "water")

    # tetrahedral intermediate (7GCH, crystal) with the leaving N on the CF3 carbon's site
    c2, c1 = L["C2"], L["C1"]
    ti = {"S:" + an: L[an] for an in ACYL}
    n_ = c2 + 1.47 * unit(c1 - c2)
    ca_ = n_ + 1.46 * unit(L["F11"] - c1)
    ti.update({"L:N": n_, "L:CA": ca_, "L:H": n_ + 1.01 * unit(L["F12"] - c1),
               "L:CB": ca_ + 1.52 * unit(ca_ - n_ + 0.8 * unit(L["F13"] - c1))})

    # ES: flatten the carbonyl C (amide plane through O, C-alpha, N), then fit onto the 1CHO P1 backbone, ring held
    I = {r.id[1]: r for ch in st["1CHO"] if ch.id == "I" for r in ch}
    p1 = {"S:C3": X("1CHO", I[18]["CA"]), "S:C2": X("1CHO", I[18]["C"]), "S:O2": X("1CHO", I[18]["O"]), "L:N": X("1CHO", I[19]["N"])}
    # P2 backbone too (Thr17 of OMTKY3 = the substrate's Leu), so the substrate keeps its beta-sheet with Ser214-Gly216
    p2 = {"S:N3": X("1CHO", I[18]["N"]), "S:CN1": X("1CHO", I[17]["C"]), "S:ON1": X("1CHO", I[17]["O"]),
          "S:CN2": X("1CHO", I[17]["CA"]), "S:NL1": X("1CHO", I[17]["N"])}
    flat = dict(ti)
    flat["S:C2"] = plane_project(ti["S:C2"], ti["S:O2"], ti["S:C3"], ti["L:N"])
    flat["S:O2"] = flat["S:C2"] + 1.23 * unit(ti["S:O2"] - flat["S:C2"])
    flat["L:N"] = flat["S:C2"] + 1.33 * unit(ti["L:N"] - flat["S:C2"])
    anchor = {**p1, **p2}
    Pm = np.array([flat[k] for k in anchor] + [ring]); Qm = np.array([anchor[k] for k in anchor] + [ring])
    R_es, t_es = wkabsch(Pm, Qm, [1.0] * len(p1) + [0.5] * len(p2) + [2.0])
    es = {k: v @ R_es.T + t_es for k, v in flat.items()}
    shift_ln = es["L:N"] - flat["L:N"] @ R_es.T - t_es                                  # (zero; kept for clarity)
    for k in ("L:CA", "L:CB", "L:H"):                                                   # keep the leaving group on its N
        es[k] = es[k] + shift_ln
    ring_es = ring @ R_es.T + t_es

    # free enzyme: the substrate waits outside the pocket (hidden), out along the pocket axis
    out_dir = unit(ti["S:C3"] - ring)
    away = {k: v + 10.0 * out_dir for k, v in es.items()}

    # acyl-enzyme: ester C flattened in the plane of O-gamma, O, C-alpha; the leaving group drifts out with its new H
    og_ti = E("Ser195:OG", "acyl")
    acyl = dict(ti)
    acyl["S:C2"] = plane_project(ti["S:C2"], og_ti, ti["S:O2"], ti["S:C3"])
    acyl["S:O2"] = acyl["S:C2"] + 1.21 * unit(ti["S:O2"] - acyl["S:C2"])
    leave_dir = unit(ti["L:N"] - ti["S:C2"])
    for k in ("L:N", "L:CA", "L:CB", "L:H"):
        acyl[k] = ti[k] + 2.5 * leave_dir
    gone = {k: ti[k] + 10.0 * leave_dir for k in ("L:N", "L:CA", "L:CB", "L:H")}

    for an in ACYL:
        k = "S:" + an
        put(k, ["free", "free2"], away[k]); put(k, ["es", "ep2"], es[k]); put(k, ["ti1", "ti2"], ti[k])
        put(k, ["acyl", "water"], acyl[k])
    for k, _ in LEAV:
        put(k, "free", away[k]); put(k, "es", es[k]); put(k, "ti1", ti[k]); put(k, "acyl", acyl[k])
        for s in ("water", "ti2", "ep2", "free2"): put(k, s, gone[k])

    # water: ~3.0 A from the ester C, 2.8 A from His57 N-epsilon2, on the leaving group's side
    ne2_w = E("His57:NE2", "water")
    w = place([(acyl["S:C2"], 3.0, 1.0), (ne2_w, 2.8, 1.0), (acyl["S:O2"], 3.4, 0.3), (og_ti, 3.0, 0.3)],
              acyl["S:C2"] + 3.0 * leave_dir)
    put("W:O", "water", w)
    put("W:O", ["free", "es", "ti1", "acyl"], w + 8.0 * leave_dir)                  # hidden: comes in from outside
    put("W:O", "ti2", ti["S:C2"] + 1.43 * unit(ti["L:N"] - ti["S:C2"]))            # the new C-OH, on the N's old site
    put("W:O", "ep2", es["S:C2"] + 1.26 * unit(es["L:N"] - es["S:C2"]))             # carboxylate O
    put("W:O", "free2", away["S:C2"] + 1.26 * unit(es["L:N"] - es["S:C2"]))
    # H2: the water proton His57 takes, then hands to Ser195 O-gamma. W:H: the water's other H, which stays on the O
    put("H2", "water", w + 0.97 * unit(ne2_w - w))
    put("H2", "ti2", E("His57:NE2", "ti2") + 1.03 * unit(E("Ser195:OG", "ti2") - E("His57:NE2", "ti2")))
    put("H2", "ep2", E("Ser195:OG", "ep2") + 0.97 * unit(E("His57:NE2", "ep2") - E("Ser195:OG", "ep2")))
    put("H2", "free2", E("Ser195:OG", "free2") + 0.97 * unit(E("His57:NE2", "free2") - E("Ser195:OG", "free2")))
    for s in ("free", "es", "ti1", "acyl"): put("H2", s, P["W:O"][SI[s]] + 0.97 * unit(ne2_w - w))
    side = unit(np.cross(unit(ne2_w - w), unit(acyl["S:C2"] - w)))
    wh = lambda o, away_from: o + 0.97 * unit(side * 0.94 - 0.34 * unit(away_from - o))
    put("W:H", "water", wh(w, ne2_w))
    for s in ("free", "es", "ti1", "acyl"): put("W:H", s, P["W:O"][SI[s]] + (P["W:H"][SI["water"]] - w))
    put("W:H", "ti2", wh(P["W:O"][SI["ti2"]], ti["S:C2"]))
    for s in ("ep2", "free2"): put("W:H", s, P["W:O"][SI[s]] + (P["W:H"][SI["ti2"]] - P["W:O"][SI["ti2"]]))   # lost to solvent: fades with its O
    # H1: Ser195's own proton -> His57 (ti1) -> the leaving N (acyl) -> away with product 1
    put("H1", "free", E("Ser195:OG", "free") + 0.97 * unit(E("His57:NE2", "free") - E("Ser195:OG", "free")))
    put("H1", "es", E("Ser195:OG", "es") + 0.97 * unit(E("His57:NE2", "es") - E("Ser195:OG", "es")))
    put("H1", "ti1", E("His57:NE2", "ti1") + 1.03 * unit(ti["L:N"] - E("His57:NE2", "ti1")))
    lone = unit(ti["L:N"] - E("His57:NE2", "ti1"))
    put("H1", "acyl", acyl["L:N"] - 1.01 * lone)
    for s in ("water", "ti2", "ep2", "free2"): put("H1", s, gone["L:N"] - 1.01 * lone)

    # ---------------- bonds: order per state
    bonds = []
    def bond(a, b, orders):
        bonds.append({"a": idx[a], "b": idx[b], "order": [float(o) for o in orders]})
    ALL = [1] * NS
    other_o = "Asp102:OD2" if asp_o == "Asp102:OD1" else "Asp102:OD1"
    for res, (n, names) in ENZ.items():
        for i, x in enumerate(names):
            for y in names[i + 1:]:
                if dist(E(f"{res}:{x}", "free"), E(f"{res}:{y}", "free")) < 1.9:
                    dbl = (res == "His57" and {x, y} in ({"CG", "CD2"}, {"CE1", "NE2"})) or \
                          (res == "Asp102" and {f"{res}:{x}", f"{res}:{y}"} == {"Asp102:CG", other_o})
                    bond(f"{res}:{x}", f"{res}:{y}", [2 if dbl else 1] * NS)
    bond("Gly193:N", "Gly193:H", ALL); bond("Ser195:N", "Ser195:H", ALL); bond("His57:ND1", "His57:HD1", ALL)
    bond("His57:HD1", asp_o, [0.5] * NS)                                            # Asp102-His57 hydrogen bond
    for i, x in enumerate(ACYL):
        for y in ACYL[i + 1:]:
            if {x, y} == {"C2", "O2"}: continue                                      # by state, below
            if dist(ti["S:" + x], ti["S:" + y]) < 1.9:
                dbl = {x, y} in ({"CN1", "ON1"}, {"CL1", "OL1"}, {"CP1", "CP2"}, {"CP3", "CP4"}, {"CP5", "CP6"})
                bond("S:" + x, "S:" + y, [2 if dbl else 1] * NS)
    #                         free es ti1 acyl water ti2 ep2 free2
    bond("S:C2", "S:O2",      [2, 2, 1, 2, 2, 1, 2, 2])        # C=O; C-O(-) in the intermediates
    bond("S:C2", "Ser195:OG", [0, 0, 1, 1, 1, 1, 0, 0])        # the acyl-enzyme ester bond
    bond("S:C2", "L:N",       [1, 1, 1, 0, 0, 0, 0, 0])        # the scissile bond
    bond("S:C2", "W:O",       [0, 0, 0, 0, 0, 1, 1, 1])        # the water O on the carbon
    bond("L:N", "L:CA", ALL); bond("L:CA", "L:CB", ALL); bond("L:N", "L:H", ALL)
    bond("H1", "Ser195:OG",   [1, 1, 0, 0, 0, 0, 0, 0])
    bond("H1", "His57:NE2",   [0.5, 0.5, 1, 0, 0, 0, 0, 0])    # hydrogen bond, then His57 N-H+
    bond("H1", "L:N",         [0, 0, 0.5, 1, 1, 1, 1, 1])
    bond("H2", "W:O",         [1, 1, 1, 1, 1, 0, 0, 0])
    bond("H2", "His57:NE2",   [0, 0, 0, 0, 0.5, 1, 0.5, 0.5])
    bond("H2", "Ser195:OG",   [0, 0, 0, 0, 0, 0.5, 1, 1])
    bond("W:H", "W:O", ALL)
    bond("Gly193:H", "S:O2",  [0, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0])   # the oxyanion hole
    bond("Ser195:H", "S:O2",  [0, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0])

    # ---------------- visibility per state (0 hidden; positions in hidden states are where an atom comes from or goes)
    for a in atoms:
        n = a["name"]; v = [1] * NS
        if n.startswith("S:"): v = [0, 1, 1, 1, 1, 1, 1, 0]
        if n.startswith("L:"): v = [0, 1, 1, 1, 0, 0, 0, 0]
        if n == "H1": v = [1, 1, 1, 1, 0, 0, 0, 0]
        if n == "H2": v = [0, 0, 0, 0, 1, 1, 1, 1]
        if n == "W:O": v = [0, 0, 0, 0, 1, 1, 1, 0]
        if n == "W:H": v = [0, 0, 0, 0, 1, 1, 0, 0]
        a["vis"] = v
        assert not np.isnan(P[n]).any(), n
        a["P"] = [round(float(x), 3) for x in P[n].reshape(-1)]

    charges = [{"atom": idx["S:O2"], "charge": [0, 0, -1, 0, 0, -1, 0, 0]},
               {"atom": idx["W:O"], "charge": [0, 0, 0, 0, 0, 0, -1, 0]},
               {"atom": idx["His57:NE2"], "charge": [0, 0, 1, 0, 0, 1, 0, 0]},
               {"atom": idx[asp_o], "charge": [-1] * NS}]

    # ---------------- arrows, as in the panels: from (a, b) to (c, d); b or d = -1 means an atom (lone pair) end
    def A(s, f, t):
        fa, fb = (f.split("|") + [None])[:2]; ta, tb = (t.split("|") + [None])[:2]
        return {"state": SI[s], "fromA": idx[fa], "fromB": idx[fb] if fb else -1, "toA": idx[ta], "toB": idx[tb] if tb else -1}
    arrows = [A("es", "His57:NE2", "H1"), A("es", "Ser195:OG", "S:C2"), A("es", "S:C2|S:O2", "S:O2"),
              A("ti1", "S:O2", "S:C2|S:O2"), A("ti1", "H1|His57:NE2", "His57:NE2"), A("ti1", "S:C2|L:N", "H1"),
              A("water", "His57:NE2", "H2"), A("water", "W:O", "S:C2"), A("water", "S:C2|S:O2", "S:O2"),
              A("ti2", "S:O2", "S:C2|S:O2"), A("ti2", "H2|His57:NE2", "His57:NE2"), A("ti2", "S:C2|Ser195:OG", "H2")]

    # ---------------- landmarks for the camera: the active site and the pocket
    site = np.mean([E("Ser195:OG", "es"), E("His57:NE2", "es"), es["S:C2"]], axis=0)
    data = {"meta": {"units": "angstrom", "unityScale": 0.1, "mirrorZ": True, "frame": "1YPH", "states": STATES,
                     "generated": str(date.today()), "fits": {k: {"rmsd": v[0], "n": v[1]} for k, v in fit.items()},
                     "notes": ["Tetrahedral intermediates from 7GCH (crystal hemiketal); ES, acyl-enzyme, water and product states modeled",
                               "His57/Asp102: 1YPH free, 7GCH bound. Ser195 and Gly193: 1YPH free, 1CHO in ES and EP2, 7GCH otherwise",
                               "Hydrogens placed by geometry"]},
            "landmarks": {"site": r3(site), "pocket": r3(ring_es), "center": r3(np.mean([ref[n]["CA"].coord for n in nums], axis=0))},
            "trace": trace, "zymogen": zym, "context": context, "pocketSurface": surface, "atoms": atoms, "bonds": bonds, "charges": charges, "arrows": arrows}
    json.dump(data, open(out, "w"), separators=(",", ":"))

    # ---------------- geometry report
    rep = [f"wrote {out}: {len(atoms)} reacting atoms, {len(bonds)} bonds, {len(arrows)} arrows, "
           f"{len(trace)} trace segments ({sum(len(s['resnum']) for s in trace)} residues), {len(context)} context residues, "
           f"pocket surface {len(sv)} vertices / {len(sf)} triangles",
           f"fits on 1YPH: {fit}", f"pocket residues: {pocket}", f"Asp102 O bonded to His57: {asp_o}",
           f"ES fit: ring moved {dist(ring, ring_es):.2f} A; C2 off the 1CHO carbonyl C by {dist(es['S:C2'], p1['S:C2']):.2f} A,"
           f" O2 by {dist(es['S:O2'], p1['S:O2']):.2f}, N by {dist(es['L:N'], p1['L:N']):.2f}",
           "state   OG..C2  C2-N   C2-Ow  O2..N193 O2..N195 Asp..His NE2..H1 NE2..H2"]
    for s in STATES:
        g = lambda n: P[n][SI[s]]
        rep.append(f"{s:6s}  {dist(g('Ser195:OG'), g('S:C2')):5.2f}  {dist(g('S:C2'), g('L:N')):5.2f}  {dist(g('S:C2'), g('W:O')):5.2f}"
                   f"  {dist(g('S:O2'), g('Gly193:N')):6.2f}  {dist(g('S:O2'), g('Ser195:N')):6.2f}  {dist(g(asp_o), g('His57:ND1')):6.2f}"
                   f"  {dist(g('His57:NE2'), g('H1')):6.2f}  {dist(g('His57:NE2'), g('H2')):6.2f}")
    rep.append(f"water: O..C2 {dist(w, acyl['S:C2']):.2f}, O..NE2 {dist(w, ne2_w):.2f}, O..OG {dist(w, og_ti):.2f}")
    # close contacts: shown substrate/water heavy atoms within 2.6 A of a protein heavy atom (1YPH) other than the four residues
    prot = [(n, a.get_id(), a.coord) for n in ref for a in ref[n] if a.element != "H" and n not in (57, 102, 193, 195)]
    for s in STATES:
        worst = []
        for a in atoms:
            if not a["vis"][SI[s]] or a["elem"] == "H" or a["cls"] == "enzyme": continue
            p = P[a["name"]][SI[s]]
            for n, an, q in prot:
                dd = dist(p, q)
                if dd < 2.6: worst.append(f"{a['name']}-{n}{an} {dd:.2f}")
        if worst: rep.append(f"close contacts in {s}: " + ", ".join(worst[:8]))
    print("\n".join(rep))


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
