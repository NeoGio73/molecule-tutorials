# Molecule tutorials

Interactive 3D tutorials on protein structure and mechanism for undergraduate biochemistry, built in Unity from
Protein Data Bank structures. Each tutorial exists in two forms: a narrated video and an interactive browser twin
(the same scene with the camera handed to the student, plus checkpoint questions).

| Tutorial | Twin | Structures | Status |
| --- | --- | --- | --- |
| Hemoglobin: T to R, the Bohr effect, sickle cell | [open](https://neogio73.github.io/molecule-tutorials/hemoglobin/) | 2HHB, 1HHO, 2HBS | prototype, placeholder narration timings |

![Deoxyhemoglobin (2HHB)](docs/images/hemoglobin_T_state.png)

![R state with the T-state ghost](docs/images/hemoglobin_interface_ghost.png)

## How the twin works

Orbit with the mouse or touch, scrub the **T ↔ R** slider, toggle chains, hemes, ion pairs, O₂ and the sickle
fiber, or replay any shot. Track A follows the mechanism (for a majors biochemistry course); Track B is the
structure-level version with the sickle-cell story. Nothing is recorded or sent anywhere.

## Pipeline

`pipeline/hb_prep.py` reads the PDB files, rebuilds the oxy tetramer from the crystal symmetry, superposes R on T by
the α1β1 dimer, and writes `hemoglobin.json` for Unity (Cα traces for both states, hemes, O₂, named residues, the
Lehninger Fig. 5-9 ion pairs, and the sickle double strand). `hemoglobin_report.txt` lists the numbers it measured
(α2β2 rotation 13°, Fe 0.33–0.45 Å out of the heme plane in T, ion-pair distances in T and R).

`pipeline/unity_scripts/` holds the Unity C# that turns the JSON into the scene: tube morphing, ball-and-stick,
ion-pair lines, the shot sequence, the 2D panel overlay, the orbit camera and the checkpoint UI. The Unity project files are not in this
repository; they are available from the author on request.

## 2D panels

The flat figures that slide in beside the 3D scene live in `pipeline/panels/`:

- `make_panels.py` (with `cdxml.py` and `heme_template.py`) writes the ChemDraw drawings as CDXML: heme b, the heme
  side view in T and R, Glu6 vs Val6, and the His146β–Asp94β pair with the Bohr equilibrium. They are opened, checked
  and exported to SVG in ChemDraw; the masters are in `chemdraw/`.
- `binding_curve.py` computes the O₂ binding curves on Lehninger's axes from the Hill equation (Hb P₅₀ 3.5 kPa,
  n_H = 3; Mb P₅₀ 0.26 kPa).
- `ion_network.py` draws the Fig. 5-9 ion pairs with their T and R distances read from `hemoglobin.json`.
- `web_svg.py` makes web-safe copies (Greek letters as Unicode, Arial with fallbacks); those are in `svg/`.

One finding is kept visible rather than smoothed over: in 2HHB/1HHO the Arg141α carboxylate–Val1α′ contact named in
Lehninger Fig. 5-9 does not break (4.8 Å in T, 4.2 Å in R), while Arg141α carboxylate–Lys127α′ does (2.8 → 4.5 Å).
The narration follows Lehninger; the 3D scene draws only the pairs that break, and the report lists all of them.

Residue numbers are written as superscripts throughout (His¹⁴⁶, Asp⁹⁴), as Lehninger does.

## Credits

Structures from the RCSB Protein Data Bank (2HHB Fermi & Perutz 1984; 1HHO Shaanan 1983; 2HBS Harrington et al.
1997). Residue naming and reference values follow Lehninger Principles of Biochemistry.

John Picione, Daytona State College.
