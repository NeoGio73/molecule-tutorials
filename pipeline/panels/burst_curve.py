"""Pre-steady-state burst for chymotrypsin + p-nitrophenylacetate, on the axes of Lehninger Fig. 6-25.
Model: P(t) = A (1 - exp(-k_burst t)) + v_ss t  (mol p-nitrophenol per mol enzyme).
A and v_ss are read off the textbook figure (burst intercept ~0.8, slope ~0.8 per min); k_burst only sets how sharp the knee is."""
import sys, numpy as np, matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import matplotlib.ticker

PARAMS = dict(A=0.78, k_burst=12.0, v_ss=0.80)   # mol/mol, 1/min, mol/mol/min

def draw(out_base):
    P = PARAMS
    plt.rcParams.update({"font.family": "Liberation Sans", "svg.fonttype": "none", "font.size": 10,
                         "mathtext.fontset": "custom", "mathtext.rm": "Liberation Sans", "mathtext.it": "Liberation Sans:italic"})
    fig, ax = plt.subplots(figsize=(4.6, 3.3))
    t = np.linspace(0, 3, 600)
    y = P["A"] * (1 - np.exp(-P["k_burst"] * t)) + P["v_ss"] * t
    ax.plot(t, y, color="k", lw=1.8)
    ax.plot([0, 0.6], [P["A"], P["A"] + P["v_ss"] * 0.6], color="k", lw=0.9, ls=(0, (4, 2)))   # extrapolated steady state
    # burst height = amount of enzyme (nearly stoichiometric)
    ax.annotate("", xy=(0.07, P["A"]), xytext=(0.07, 0.0), arrowprops=dict(arrowstyle="<->", lw=0.9, color="0.35"))
    ax.text(0.14, 0.30, "burst ≈ 1 mol per\nmol of enzyme\n(fast acylation)", fontsize=8.5, va="center")
    ax.text(1.95, 2.05, "steady state\n(slow deacylation)", fontsize=8.5, va="top", ha="left")
    ax.set_xlim(0, 3); ax.set_ylim(0, 3.0)
    ax.set_xticks([0, 1, 2, 3]); ax.set_yticks([0, 1.0, 2.0, 3.0])
    ax.set_xlabel("Time (min)"); ax.set_ylabel("$\\it{p}$-Nitrophenol (mol/mol of enzyme)")
    ax.yaxis.set_major_formatter(matplotlib.ticker.FormatStrFormatter("%.1f"))
    for s in ("top", "right"): ax.spines[s].set_visible(False)
    fig.tight_layout()
    fig.savefig(out_base + ".svg"); fig.savefig(out_base + "_preview.png", dpi=200)

if __name__ == "__main__":
    draw(sys.argv[1] if len(sys.argv) > 1 else "ch_p08_burst")
