"""WebVTT captions for the two narrated videos.

Text: the narration script (narration/scripts.json), with the few places where the recording says something
different (EDITS below). Timing: word times from speech recognition (NVIDIA Parakeet TDT 0.6B v2 via sherpa-onnx)
run on the cleaned clips, aligned to the script words; each clip starts in the video at its shot start minus 1/30 s
(the same offsets used to mux the audio).

Usage: python3 captions.py <clip folder> <out folder>
"""
import json, re, sys, difflib
import numpy as np, soundfile as sf, scipy.signal as ss, sherpa_onnx
from num2words import num2words

M = "/tmp/asr/models/sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8/"
rec = sherpa_onnx.OfflineRecognizer.from_transducer(encoder=M + "encoder.int8.onnx", decoder=M + "decoder.int8.onnx",
        joiner=M + "joiner.int8.onnx", tokens=M + "tokens.txt", model_type="nemo_transducer", num_threads=4)
SCRIPTS = json.load(open("/home/claude/narration/scripts.json"))
VIDEOS = {
    "hemoglobin_narrated_v3": ("hemo", "hb_shot{}", [0, 21.9, 61.5, 90.2, 140.6, 161.6, 198.8]),
    "chymotrypsin_narrated_v1": ("chymo", "ch_shot{:02d}", [0, 29.6, 60.9, 87.8, 136.4, 158.8, 185.7, 216.2, 244.7, 255.5]),
}
# Where the recording differs from the script (checked against the transcripts): (tutorial, shot, script text, spoken text)
EDITS = json.load(open("/home/claude/narration/caption_edits.json")) if len(sys.argv) > 3 else []

def norm_tokens(text):
    """Script text -> spoken-form tokens, with the index of the script word each comes from."""
    words = text.split()
    out = []
    for i, w in enumerate(words):
        w2 = w.lower().replace("’", "'")
        w2 = re.sub(r"(\d+),(\d+)", r"\1 \2", w2)                        # 2,3-bisphosphoglycerate
        for part in re.split(r"[-–/\s]+", w2):
            part = re.sub(r"[^a-z0-9']", "", part)
            if not part: continue
            if part.isdigit():
                for p in re.split(r"[\s-]+", num2words(int(part)).replace(" and ", " ")):
                    out.append((p, i))
            else:
                out.append((part, i))
    return words, out

def asr_words(path):
    x, sr = sf.read(path, dtype="float32")
    if x.ndim > 1: x = x.mean(axis=1)
    x16 = ss.resample_poly(x, 1, 3).astype(np.float32)
    hop = 160
    e = np.array([np.sqrt(np.mean(x16[i:i + hop] ** 2)) for i in range(0, len(x16) - hop, hop)])
    d = 20 * np.log10(e + 1e-9); sp = d > np.percentile(d, 10) + 10
    cuts, run = [0], 0                                   # cut at the middle of pauses of 0.3 s or more
    for i, v in enumerate(sp):
        if not v: run += 1
        else:
            if run >= 30: cuts.append((i - run // 2) * hop)
            run = 0
    cuts.append(len(x16))
    chunks, s = [], 0
    for c in cuts[1:]:
        if c - s > 12 * 16000 or c == len(x16):
            prev = max([k for k in cuts if s < k <= c and k - s <= 12 * 16000] or [c])
            chunks.append((s, prev)); s = prev
            if prev != c and c == len(x16): chunks.append((s, c)); s = c
    words = []
    for a, b in chunks:
        if b - a < 0.3 * 16000: continue
        st = rec.create_stream(); st.accept_waveform(16000, x16[a:b]); rec.decode_stream(st)
        r = st.result; cur = None
        for tok, t in zip(r.tokens, r.timestamps):
            t = a / 16000 + t
            if tok.startswith(" ") or cur is None:
                if cur: words.append(cur)
                cur = {"w": tok.strip(), "t": t, "chunk_end": b / 16000}
            else:
                cur["w"] += tok
        if cur: words.append(cur)
    # end of each word: start of the next word in the same chunk, else the chunk end (capped)
    for i, w in enumerate(words):
        nxt = words[i + 1]["t"] if i + 1 < len(words) and words[i + 1]["chunk_end"] == w["chunk_end"] else None
        w["e"] = min(nxt if nxt is not None else w["chunk_end"], w["t"] + 0.9)
    return words

def asr_norm(words):
    out = []
    for j, w in enumerate(words):
        for p in re.split(r"[-\s]+", w["w"].lower()):
            p = re.sub(r"[^a-z0-9']", "", p)
            if p.isdigit():
                for q in re.split(r"[\s-]+", num2words(int(p)).replace(" and ", " ")): out.append((q, j))
            elif p: out.append((p, j))
    return out

def cues_for_clip(text, words, offset):
    swords, stoks = norm_tokens(text)
    atoks = asr_norm(words)
    sm = difflib.SequenceMatcher(a=[t for t, _ in stoks], b=[t for t, _ in atoks], autojunk=False)
    t_start = [None] * len(swords); t_end = [None] * len(swords)
    mism = []
    for tag, i1, i2, j1, j2 in sm.get_opcodes():
        if tag == "equal":
            for k in range(i2 - i1):
                si = stoks[i1 + k][1]; aj = atoks[j1 + k][1]
                if t_start[si] is None: t_start[si] = words[aj]["t"]
                t_end[si] = words[aj]["e"]
        else:
            mism.append((" ".join(t for t, _ in stoks[i1:i2]), " ".join(words[j]["w"] for j in sorted({a for _, a in atoks[j1:j2]}))))
            if j2 > j1 and i2 > i1:                       # spread the substituted span over the script words
                ta = words[atoks[j1][1]]["t"]; tb = words[atoks[j2 - 1][1]]["e"]
                idx = sorted({stoks[k][1] for k in range(i1, i2)})
                for n, si in enumerate(idx):
                    a = ta + (tb - ta) * n / len(idx); b = ta + (tb - ta) * (n + 1) / len(idx)
                    if t_start[si] is None: t_start[si] = a
                    t_end[si] = b
    # fill any word still without a time by interpolation
    known = [i for i in range(len(swords)) if t_start[i] is not None]
    for i in range(len(swords)):
        if t_start[i] is None:
            prev = max([k for k in known if k < i], default=None); nxt = min([k for k in known if k > i], default=None)
            a = t_end[prev] if prev is not None else 0.0
            b = t_start[nxt] if nxt is not None else a + 0.5
            t_start[i] = a; t_end[i] = b
    # caption units: sentences, split at , ; : when longer than 84 characters
    units, cur = [], []
    for i, w in enumerate(swords):
        cur.append(i)
        if re.search(r"[.?!]$", w): units.append(cur); cur = []
    if cur: units.append(cur)
    final = []
    for u in units:
        text_u = " ".join(swords[i] for i in u)
        if len(text_u) <= 84: final.append(u); continue
        piece = []
        for i in u:
            piece.append(i)
            if re.search(r"[,;:]$", swords[i]) and len(" ".join(swords[k] for k in piece)) >= 30:
                final.append(piece); piece = []
        if piece:
            if final and len(" ".join(swords[k] for k in piece)) < 25 and len(" ".join(swords[k] for k in final[-1] + piece)) <= 90:
                final[-1] = final[-1] + piece
            else: final.append(piece)
    # break any piece still over 84 characters at the word nearest its middle
    out_units = []
    for u in final:
        while len(" ".join(swords[i] for i in u)) > 84:
            half = len(" ".join(swords[i] for i in u)) / 2; acc = 0; cut = 1
            for n, i in enumerate(u):
                acc += len(swords[i]) + 1
                if acc >= half: cut = n + 1; break
            out_units.append(u[:cut]); u = u[cut:]
        out_units.append(u)
    # every cue must fit on two lines of 42 characters; split again where it does not
    fitted = []
    stack = list(reversed(out_units))
    while stack:
        u = stack.pop()
        if len(u) > 1 and max(len(l) for l in wrap(" ".join(swords[i] for i in u)).split("\n")) > 42:
            n = len(u); best = None
            for k in range(1, n):
                a = " ".join(swords[i] for i in u[:k]); b = " ".join(swords[i] for i in u[k:])
                sc = abs(len(a) - len(b)) - (15 if re.search(r"[,;:]$", swords[u[k - 1]]) else 0)
                if best is None or sc < best[0]: best = (sc, k)
            stack.append(u[best[1]:]); stack.append(u[:best[1]])
        else:
            fitted.append(u)
    out_units = fitted
    cues = []
    for u in out_units:
        s = t_start[u[0]] - 0.10; e = t_end[u[-1]] + 0.35
        cues.append([offset + max(s, 0), offset + e, " ".join(swords[i] for i in u)])
    return cues, mism

def wrap(text, width=42):
    """Two lines at most; prefer a break after punctuation, never leave a short function word at a line end."""
    if len(text) <= width: return text
    words = text.split(); best = None
    weak = {"a", "an", "the", "of", "to", "and", "in", "on", "at", "is", "its", "it", "that", "from", "with", "by", "for"}
    for k in range(1, len(words)):
        a, b = " ".join(words[:k]), " ".join(words[k:])
        if len(a) > width or len(b) > width: continue
        score = abs(len(a) - len(b))
        if re.search(r"[,;:]$", words[k - 1]): score -= 12
        if words[k - 1].lower() in weak: score += 15
        if best is None or score < best[0]: best = (score, a + "\n" + b)
    return best[1] if best else text

def ts(t):
    h = int(t // 3600); m = int(t % 3600 // 60); s = t % 60
    return f"{h:02d}:{m:02d}:{s:06.3f}"

if __name__ == "__main__":
    clipdir, outdir = sys.argv[1], sys.argv[2]
    report = []
    for video, (tut, pattern, starts) in VIDEOS.items():
        allcues = []
        for k, start in enumerate(starts, 1):
            text = SCRIPTS[tut][str(k)]
            for e in EDITS:
                if e[0] == tut and e[1] == k:
                    assert e[2] in text, (tut, k, e[2]); text = text.replace(e[2], e[3])
            words = asr_words(f"{clipdir}/{pattern.format(k)}.wav")
            cues, mism = cues_for_clip(text, words, max(0.0, start - 1 / 30))
            allcues += cues
            report.append(f"== {tut} shot {k}: " + ("; ".join(f"[{a}] -> [{b}]" for a, b in mism) or "all words matched"))
        # merge a cue shorter than 1 s into the next one when the two still fit on two lines
        merged = []
        for c in allcues:
            if merged and merged[-1][1] - merged[-1][0] < 1.0 and c[0] - merged[-1][1] < 0.6:
                t = merged[-1][2] + " " + c[2]
                if max(len(l) for l in wrap(t).split("\n")) <= 42:
                    merged[-1] = [merged[-1][0], c[1], t]; continue
            merged.append(list(c))
        allcues = merged
        # no overlaps; minimum 1 s on screen where there is room
        for i in range(len(allcues)):
            if i + 1 < len(allcues): allcues[i][1] = min(allcues[i][1], allcues[i + 1][0] - 0.04)
            if allcues[i][1] - allcues[i][0] < 1.0:
                room = (allcues[i + 1][0] - 0.04) if i + 1 < len(allcues) else allcues[i][0] + 1.0
                allcues[i][1] = min(allcues[i][0] + 1.0, room)
            if allcues[i][1] - allcues[i][0] < 1.0:      # still short: start earlier, into the pause before it
                floor = allcues[i - 1][1] + 0.04 if i > 0 else 0.0
                allcues[i][0] = max(floor, allcues[i][1] - 1.0)
        with open(f"{outdir}/{video}.vtt", "w") as f:
            f.write("WEBVTT\n\n")
            for n, (s, e, t) in enumerate(allcues, 1):
                f.write(f"{n}\n{ts(s)} --> {ts(e)}\n{wrap(t)}\n\n")
        print(video, len(allcues), "cues; last ends", ts(allcues[-1][1]))
    open(f"{outdir}/alignment_report.txt", "w").write("\n".join(report) + "\n")
