"""O2 binding curves on the Lehninger axes (pO2 in kPa, 0-16; theta 0-1).
Hill model: theta = p^n / (p^n + P50^n). Parameters are stated in PARAMS so they can be checked against the text."""
import sys, numpy as np, matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

PARAMS = dict(hb_p50=3.5, hb_n=3.0, mb_p50=0.26, tissue=4.0, lungs=13.3)   # kPa; all three checked against Lehninger (n_H = 3 from Fig. 5-13), Nov 7, 2026

def theta(p, p50, n): return p**n / (p**n + p50**n)

def draw(out_base, show_mb=True):
    P = PARAMS
    plt.rcParams.update({"font.family": "Liberation Sans", "svg.fonttype": "none", "font.size": 12, "axes.linewidth": 1.2, "xtick.major.width": 1.2, "ytick.major.width": 1.2,
                         "mathtext.fontset": "custom", "mathtext.rm": "Liberation Sans", "mathtext.it": "Liberation Sans:italic"})
    fig, ax = plt.subplots(figsize=(4.6, 3.3))
    p = np.linspace(0, 16, 800)
    if show_mb:
        ax.plot(p, theta(p, P["mb_p50"], 1.0), color="k", lw=1.5, ls=(0, (4, 2)), label="myoglobin")
    ax.plot(p, theta(p, P["hb_p50"], P["hb_n"]), color="k", lw=2.6, label="hemoglobin")
    tt, tl = theta(P["tissue"], P["hb_p50"], P["hb_n"]), theta(P["lungs"], P["hb_p50"], P["hb_n"])
    for x, lab in ((P["tissue"], "tissues"), (P["lungs"], "lungs")):
        ax.axvline(x, color="0.55", lw=1.2, ls=":")
        ax.text(x, 1.035, lab, ha="center", va="bottom", fontsize=10.5)
    # O2 delivered: drop in Hb saturation between lungs and tissues
    ax.annotate("", xy=(P["tissue"] + 0.25, tt), xytext=(P["tissue"] + 0.25, tl),
                arrowprops=dict(arrowstyle="->", lw=1.3, color="k"))
    ax.plot([P["tissue"], P["lungs"]], [tl, tl], color="0.55", lw=0.9)
    ax.text(P["tissue"] + 0.6, tt - 0.07, f"O$_2$ released\n(Δθ ≈ {tl - tt:.2f})", va="top", fontsize=10.5)
    ax.set_xlim(0, 16); ax.set_ylim(0, 1.0)
    ax.set_xticks(range(0, 17, 4)); ax.set_yticks([0, 0.2, 0.4, 0.6, 0.8, 1.0])
    ax.set_xlabel("pO$_2$ (kPa)"); ax.set_ylabel("θ")
    for s in ("top", "right"): ax.spines[s].set_visible(False)
    ax.legend(frameon=False, loc="lower right", fontsize=10.5)
    fig.tight_layout()
    fig.savefig(out_base + ".svg"); fig.savefig(out_base + "_preview.png", dpi=200)
    print(f"theta(lungs)={tl:.3f} theta(tissues)={tt:.3f} delta={tl - tt:.3f}")

if __name__ == "__main__":
    draw(sys.argv[1] if len(sys.argv) > 1 else "hb_p07_binding_curve")
