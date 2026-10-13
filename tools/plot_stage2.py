"""2단계 검증 이미지. 사용: python3 tools/plot_stage2.py <출력 폴더(settle·world png, csv)> <docs/images> <한글 글꼴>"""
import csv, statistics as st, sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFont
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib import font_manager

src, out, font = Path(sys.argv[1]), Path(sys.argv[2]), sys.argv[3]
SEEDS = [1, 2, 3]
pil_font = ImageFont.truetype(font, 22)

# 1. 대륙 지도 3장
ims = [Image.open(src / f"world_{s}.png").resize((560, 560), Image.LANCZOS) for s in SEEDS]
c = Image.new("RGB", (560 * 3 + 20, 560 + 40), (252, 252, 251))
d = ImageDraw.Draw(c)
for i, im in enumerate(ims):
    c.paste(im, (i * 570, 40))
    d.text((i * 570 + 6, 6), f"시드 {SEEDS[i]}", fill=(40, 40, 40), font=pil_font)
c.save(out / "sim_stage2_worlds.png")

# 2. 정착 과정 (시드 1): 마지막 해 기준 탐사 범위로 잘라 같은 틀에 놓는다
last = np.asarray(Image.open(src / "settle_1_y0250.png"))
mask = ~((last[:, :, 0] == 36) & (last[:, :, 1] == 38) & (last[:, :, 2] == 44))
ys, xs = np.where(mask)
box = (xs.min(), ys.min(), xs.max(), ys.max())
years = [100, 150, 200, 250]
frames = [Image.open(src / f"settle_1_y{y:04d}.png").crop(box) for y in years]
fw, fh = frames[0].size
scale = 420 / fh
frames = [f.resize((int(fw * scale), 420), Image.NEAREST) for f in frames]
c = Image.new("RGB", (frames[0].width * 4 + 30, 460), (252, 252, 251))
d = ImageDraw.Draw(c)
for i, (f, y) in enumerate(zip(frames, years)):
    c.paste(f, (i * (frames[0].width + 10), 40))
    d.text((i * (frames[0].width + 10) + 4, 6), f"{y}년", fill=(40, 40, 40), font=pil_font)
c.save(out / "sim_stage2_settlement.png")

# 3. 강 확대 (청크 경계를 넘어 이어지는지)
Image.open(src / "world_1_zoom.png").save(out / "sim_stage2_river_zoom.png")
Image.open(src / "settle_1_zoom.png").save(out / "sim_stage2_village_zoom.png")

# 4. 흉년 영향
font_manager.fontManager.addfont(font)
plt.rcParams.update({"font.family": font_manager.FontProperties(fname=font).get_name(), "axes.unicode_minus": False,
    "axes.spines.top": False, "axes.spines.right": False, "axes.grid": True, "grid.color": "#e6e5e0",
    "figure.facecolor": "#fcfcfb", "axes.facecolor": "#fcfcfb", "axes.titleweight": "bold", "figure.dpi": 110})
BLUE, ORANGE, INK2 = "#2a78d6", "#eb6834", "#52514e"
rows = {s: list(csv.DictReader(open(src / f"settle_{s}.csv"))) for s in SEEDS}
def measure(r, cond):
    idx = [i for i, x in enumerate(r) if cond(float(x["weather"])) and 30 < i < len(r) - 4]
    pop = lambda i: max(1, int(r[i]["population"]))
    return (st.mean(100 * (int(r[i + 1]["population"]) / pop(i) - 1) for i in idx),
            st.mean(1000 * int(r[i + 1]["hunger_deaths"]) / pop(i + 1) for i in idx),
            st.mean(1000 * int(r[i + 1]["births"]) / pop(i + 1) for i in idx))
bad = [measure(rows[s], lambda w: w < 0.75) for s in SEEDS]
good = [measure(rows[s], lambda w: w >= 0.95) for s in SEEDS]
fig, axes = plt.subplots(1, 3, figsize=(12, 3.8))
for k, (ax, title) in enumerate(zip(axes, ["다음 해 인구 증가 (%)", "다음 해 굶주림 사망 (인구 천 명당)", "다음 해 출생 (인구 천 명당)"])):
    vals = [st.mean(x[k] for x in good), st.mean(x[k] for x in bad)]
    bars = ax.bar(["평년", "흉년"], vals, color=[BLUE, ORANGE], width=0.55)
    for x, seeds in zip([0, 1], [good, bad]): ax.scatter([x] * 3, [v[k] for v in seeds], color="#0b0b0b", s=14, zorder=3)
    for b, v, seeds in zip(bars, vals, [good, bad]):
        ax.annotate(f"{v:.1f}", (b.get_x() + b.get_width() / 2, max(s[k] for s in seeds)), xytext=(0, 6), textcoords="offset points", ha="center", color=INK2)
    ax.set_ylim(0, max(max(s[k] for s in good + bad) * 1.25, 0.1))
    ax.set_title(title, loc="left", fontsize=11); ax.grid(axis="x", visible=False)
fig.suptitle("흉년(날씨 < 0.75)이 들면: 굶어 죽는 사람이 늘고 아이가 덜 태어나 성장이 멈춘다 (생성된 대륙, 250년, 점 = 시드)", x=0.01, ha="left", fontsize=12)
fig.tight_layout(); fig.savefig(out / "sim_stage2_famine.png"); plt.close(fig)
print("ok")
