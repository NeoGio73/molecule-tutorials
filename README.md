# Molecule tutorials

Interactive 3D tutorials on protein structure and mechanism for undergraduate biochemistry, built in Unity from
Protein Data Bank structures. Each tutorial exists in two forms: a narrated video and an interactive browser twin
(the same scene with the camera handed to the student, plus checkpoint questions).

| Tutorial | Twin | Structures | Status |
| --- | --- | --- | --- |
| Hemoglobin: T to R and the Bohr effect (sickle fiber as an optional view) | [open](https://neogio73.github.io/molecule-tutorials/hemoglobin/) | 2HHB, 1HHO, 2HBS | narrated, 3 min 42 s (narration recorded Nov 11–12, video rendered Nov 14, 2026) |
| Chymotrypsin: the catalytic triad, acylation and deacylation, burst kinetics, zymogen activation | [open](https://neogio73.github.io/molecule-tutorials/chymotrypsin/) | 1YPH, 1CHO, 7GCH, 2CGA | narrated, 5 min 12 s (narration recorded Nov 11–12, video rendered Nov 14, 2026) |

The narration is the author's own voice, one take per shot; each shot lasts its narration plus 1.5 s, and the 2D
panels and mechanism steps are placed on the words that name them. Both twins open on a **Start the tutorial** button,
because browsers play sound only after a click. The narrated videos (1920 × 1080, 30 frames per second) will be posted
as supplementary files with the preprint; their captions (WebVTT and SRT) are in `pipeline/captions/`.

![Deoxyhemoglobin (2HHB)](docs/images/hemoglobin_T_state.png)

![R state with the T-state ghost](docs/images/hemoglobin_interface_ghost.png)

## How the twin works

The shot sequence plays and stops at checkpoint questions (two tries, then the explanation). Orbit with the
mouse or touch, or replay any shot. Nothing is recorded or sent anywhere.

On a phone, or in a window smaller than 900 × 500 CSS pixels, the twin switches to a compact layout
(`CompactLayout.cs`): text and buttons are sized in CSS pixels, the controls and the shot list fold behind two
buttons (**Controls** and **Shots**), the checkpoint box spans the bottom of the screen, questions say "tap" instead
of "click", and in portrait the camera keeps the shot's horizontal field of view.

- **Hemoglobin:** scrub the **T ↔ R** slider; toggle chains, hemes, ion pairs, O₂ and the sickle fiber.
- **Chymotrypsin:** step through the eight states of Lehninger Fig. 6-27 with the 2D panel for each; toggle the
  curved arrows, labels, the hydrophobic pocket and the chains; click a triad residue for its role; at the
  substrate-binding checkpoint, swap the substrate's Phe for Tyr, Trp, Lys, Glu or Gly and see which fit the pocket
  (a schematic animation, not computed docking).

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
- `ion_pair_distances.py` measures the Fig. 5-9 ion pairs the usual way (closest N–O between the charged groups, in
  each state) for the hemoglobin ion-pair panel; the same values are in the paper's Table 2 and the structure report.
- `restyle_panels.py` makes the versions shown in the tutorials' panel card (in `card/`): heavier lines, so the
  drawings stay black when the card shrinks them; O and N in atom labels in the 3D model's element colors (O red,
  N blue), except in the leaving half, which keeps Lehninger's blue letters; and the drawing's title moved to the
  card's title strip. Only colors and line weights change; atoms, bonds and positions are as exported.

One finding is kept visible rather than smoothed over. Lehninger Fig. 5-9 pairs the Arg¹⁴¹α carboxylate with the
Val¹α′ amino group, as in Perutz's 1970 model. In three deoxyhemoglobin crystal structures (2HHB, 2DN2, 1A3N) the
two groups are 4.6–7.1 Å apart, too far for a salt bridge, and the carboxylate pairs with Lys¹²⁷α′ instead
(2.6–2.8 Å), as Fermi first noted when he refined the deoxy structure in 1975. In R the α termini are mobile: poorly
ordered in 1HHO, and Val¹α is not located in 2DN1, so no R-state value is given for that contact. The narration
follows Lehninger (the C-terminal arginine of α still makes two ion pairs in T); the 3D scene draws only the pairs that
break, the ion-pair panel shows the Val¹α′ row dashed, and the reports list all of them.

`pipeline/ion_pairs_multi.py` measures the Fig. 5-9 ion pairs in any set of hemoglobin entries, at both α1–α2
interfaces, with B factors, alternate conformations and any water or ion within 3.6 Å of the termini:
`python3 -I ion_pairs_multi.py <folder with the .pdb files> T:2HHB T:2DN2 T:1A3N R:1HHO R:2DN1`. The output for those
five entries is in `hemoglobin_ion_pairs_5structures.txt`; the PDB files themselves are downloaded from the RCSB.

Residue numbers are written as superscripts throughout (His¹⁴⁶, Asp⁹⁴), as Lehninger does.

### Chymotrypsin

- `make_chymo.py` writes the seven mechanism frames of Lehninger Fig. 6-27 as CDXML: free enzyme, ES complex, the
  short-lived intermediate of acylation, the acyl-enzyme (with product 1 leaving, then with water), the short-lived
  intermediate of deacylation, and the enzyme–product 2 complex. They follow the book's layout and colors (leaving half
  blue, acyl half black, the proton that moves magenta, arrows blue). Two additions to the book's figure: a third arrow
  in the ES complex (His⁵⁷ takes the Ser¹⁹⁵ proton) and Asp¹⁰², which the narration's low-barrier hydrogen bond needs.
  Masters in `chemdraw/chymo/`, web copies in `svg/chymo/`.
- The substrate backbone (AAₙ–C–CH–NH–C–CH–NH–AAₙ) is written as text with plain bond lines. As ChemDraw labels, a CH
  or NH bonded on both sides gets its H stacked on top, and ChemDraw's SVG export then misplaces the C or N.
- `common_frame.py` gives the exported frames one shared viewBox, so they line up when one replaces the next.
- `burst_curve.py` draws the pre-steady-state burst on the axes of Lehninger Fig. 6-25.
- `make_chymo.py` also writes ch_p02 (which side chains fit the hydrophobic pocket) and ch_p09 (zymogen activation after
  Lehninger Fig. 6-42: chymotrypsinogen, trypsin's cut of the Arg¹⁵–Ile¹⁶ scissile bond giving π-chymotrypsin with its
  Ile¹⁶–Asp¹⁹⁴ ion pair, then α-chymotrypsin after Ser¹⁴–Arg¹⁵ and Thr¹⁴⁷–Asn¹⁴⁸ are cut out).

#### Chymotrypsin structures and scene data

- `chymo_measure.py` superposes 1CHO, 7GCH and 2CGA on 1YPH and measures what the narration claims: the oxyanion hole
  (Gly¹⁹³ and Ser¹⁹⁵ N–H), the attack geometry, the residues lining the hydrophobic pocket, the Asp¹⁰²–His⁵⁷ hydrogen
  bond in each structure, and what moves on activation. Results in `chymotrypsin_report.txt`.
- `chymo_prep.py` writes `chymotrypsin.json` for the Unity scene: the 1YPH chain trace, the zymogen trace for the
  activation coda, the pocket residues and a translucent pocket surface, and every reacting atom with a position, bonds,
  charges and curved arrows in each of the eight states of Lehninger Fig. 6-27. The tetrahedral intermediates come
  from the 7GCH hemiketal (crystal); the ES complex is fitted to the 1CHO P1/P2 backbone; the acyl-enzyme, water and
  product states are modeled. The script's header lists every modeling choice.
- `unity_scripts/Ch*.cs` build and play the scene: `ChSceneBuilder` (tubes, pocket surface, labels), `ChMechanism`
  (atoms move, bonds form and break, protons hop, arrows draw themselves on), `ChLabels` (residue labels with
  superscript numbers), `ChShotSequence` (camera and timing per storyboard shot), and `ChRimSurface.shader` (the
  pocket surface). They reuse `ChainTube`, `StickGroup`, `HbStyle` and `HbPanelOverlay` from the hemoglobin scripts.
  `ChShotSequence` also covers the optional activation coda (shot 10) and times the 2D panels to the shots.
  `ChTutorialController` is the interactive twin (checkpoints, step slider, toggles, residue roles, shot replay) and
  `ChSideChainPicker` the side-chain swap at the substrate-binding checkpoint.
- Optional: `ChShapesDrawer.cs` draws the curved arrows, dashed hydrogen bonds and circled charges with
  [Shapes](https://acegikmo.com/shapes) (Freya Holmér, Unity Asset Store), a paid asset that is not included here. It
  compiles only with the scripting define `CH_SHAPES` and needs the Shapes render feature on the camera's URP renderer.
  For WebGL builds, turn off "immediate mode instancing" in the Shapes settings; with it on, the dashed lines did not
  render in our WebGL test. Without Shapes, `ChMechanism` draws its own tube arrows, cylinder dashes and charges.

## Building the twins

`pipeline/unity_scripts/TutorialBuilds.cs` (an Editor script) adds a **Tutorials → Build WebGL** menu that builds
each tutorial into its own folder with its own page title (`Builds/<Tutorial>/WebGL`), Gzip-compressed with the
decompression fallback that GitHub Pages needs. The page comes from `WebGLTemplates/Tutorial` (copy it to
`Assets/WebGLTemplates/Tutorial`): the twin fills the browser window. The contents of each build folder go in the
tutorial's folder here (`hemoglobin/`, `chymotrypsin/`).

## Credits

Structures from the RCSB Protein Data Bank (2HHB Fermi & Perutz 1984; 1HHO Shaanan 1983; 2HBS Harrington et al.
1997; 1YPH Razeto et al.; 1CHO Fujinaga et al. 1987; 7GCH Brady et al. 1990; 2CGA Wang, Bode & Huber 1985; used only
for the ion-pair check: 2DN2 and 2DN1 Park et al. 2006, 1A3N Tame & Vallone 2000). Residue naming and reference values follow Lehninger Principles of Biochemistry.

John Picione, Daytona State College.
