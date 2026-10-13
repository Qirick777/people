"""1단계 검증 장면 그래프. 사용: python3 tools/plot_stage1.py <CSV 폴더> <출력 폴더> <한글 글꼴 경로>"""
import csv, statistics as st, sys
from pathlib import Path
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib import font_manager

src, out, font = Path(sys.argv[1]), Path(sys.argv[2]), sys.argv[3]
font_manager.fontManager.addfont(font)
plt.rcParams.update({
    "font.family": font_manager.FontProperties(fname=font).get_name(),
    "axes.unicode_minus": False, "figure.dpi": 110, "savefig.dpi": 110,
    "axes.spines.top": False, "axes.spines.right": False,
    "axes.edgecolor": "#b8b7b1", "axes.labelcolor": "#52514e", "xtick.color": "#52514e", "ytick.color": "#52514e",
    "axes.grid": True, "grid.color": "#e6e5e0", "grid.linewidth": 0.8, "axes.titlesize": 12, "axes.titleweight": "bold",
    "figure.facecolor": "#fcfcfb", "axes.facecolor": "#fcfcfb", "lines.linewidth": 2,
})
BLUE, ORANGE, AQUA, INK2 = "#2a78d6", "#eb6834", "#1baf7a", "#52514e"
SEEDS = [1, 2, 3]

def load(name): return list(csv.DictReader(open(src / name)))
def col(rows, k): return [float(r[k]) for r in rows]
def roll(v, n=10):
    out_, s = [], 0.0
    for i, x in enumerate(v):
        s += x
        if i >= n: s -= v[i - n]
        out_.append(s / min(i + 1, n))
    return out_
def mean_series(series):
    n = min(len(s) for s in series)
    return [st.mean(s[i] for s in series) for i in range(n)]

def seed_lines(ax, series, color, label=None):
    for s in series: ax.plot(range(1, len(s) + 1), s, color=color, alpha=0.22, linewidth=1)
    m = mean_series(series)
    ax.plot(range(1, len(m) + 1), m, color=color, label=label)
    return m

# 1. 선택: 성장 실행 250년
fig, axes = plt.subplots(2, 2, figsize=(11, 7))
for ax, (k, title) in zip(axes.flat, [("constitution", "체질 (유전 점수) · 사망률이 걸러 낸다"), ("looks", "외모 · 짝 점수가 고른다"),
                                       ("height", "키 (유전 점수) · 짝 점수가 고른다"), ("twinning_freq", "쌍둥이 체질 대립유전자 빈도")]):
    seed_lines(ax, [roll(col(load(f"grow_{s}.csv"), k)) for s in SEEDS], BLUE)
    ax.set_title(title, loc="left")
    ax.set_xlabel("년")
fig.suptitle("자연선택과 성선택: 넓은 섬, 부부 10쌍에서 250년 (시드 3개 평균, 10년 이동평균)", x=0.01, ha="left", fontsize=13)
fig.tight_layout()
fig.savefig(out / "sim_stage1_selection.png"); plt.close(fig)

# 2. 인구
fig, axes = plt.subplots(1, 2, figsize=(11, 4))
seed_lines(axes[0], [col(load(f"island_{s}.csv"), "population") for s in SEEDS], BLUE)
axes[0].set_title("작은 섬 400필지, 부부 5쌍 → 1,250년 (50세대)", loc="left"); axes[0].set_xlabel("년"); axes[0].set_ylabel("인구")
seed_lines(axes[1], [col(load(f"grow_{s}.csv"), "population") for s in SEEDS], BLUE)
axes[1].set_title("넓은 섬 9,000필지, 부부 10쌍 → 250년 (130년쯤 땅이 참)", loc="left"); axes[1].set_xlabel("년")
fig.suptitle("인구: 땅이 남아 있는 동안 연 약 1.6% 늘고, 땅이 차면 흉년과 굶주림으로 출렁인다", x=0.01, ha="left", fontsize=13)
fig.tight_layout(); fig.savefig(out / "sim_stage1_population.png"); plt.close(fig)

# 3. 성선택 실험
fig, ax = plt.subplots(figsize=(9, 4.5))
hi = seed_lines(ax, [roll(col(load(f"sexsel_high_{s}.csv"), "height"), 20) for s in SEEDS], BLUE, "모두 키 큰 상대를 선호")
ne = seed_lines(ax, [roll(col(load(f"sexsel_neutral_{s}.csv"), "height"), 20) for s in SEEDS], ORANGE, "선호 없음")
ax.annotate("키 선호", (len(hi), hi[-1]), xytext=(6, 0), textcoords="offset points", color=INK2, va="center")
ax.annotate("선호 없음", (len(ne), ne[-1]), xytext=(6, 0), textcoords="offset points", color=INK2, va="center")
ax.set_xlim(0, len(hi) * 1.1)
ax.set_title("성선택 실험: 키 유전 점수 평균 (미혼 600명, 6,000필지, 400년, 시드 3개, 20년 이동평균)", loc="left")
ax.set_xlabel("년"); ax.legend(frameon=False, loc="upper left")
fig.tight_layout(); fig.savefig(out / "sim_stage1_sexsel.png"); plt.close(fig)

# 4. 재산과 자식 수
quint = []
for s in SEEDS:
    men = sorted(load(f"grow_{s}_men.csv"), key=lambda r: float(r["wealth"]))
    n = len(men)
    quint.append([st.mean(int(r["children"]) for r in men[i * n // 5:(i + 1) * n // 5]) for i in range(5)])
q = [st.mean(x[i] for x in quint) for i in range(5)]
fig, ax = plt.subplots(figsize=(8, 4.2))
labels = ["하위 20%", "20~40%", "40~60%", "60~80%", "상위 20%"]
bars = ax.bar(labels, q, color=BLUE, width=0.6)
for b, v in zip(bars, q): ax.annotate(f"{v:.1f}", (b.get_x() + b.get_width() / 2, v), xytext=(0, 3), textcoords="offset points", ha="center", color=INK2)
ax.set_title("40세 넘게 산 남성의 자식 수, 죽을 때 가구 재산 구간별 (넓은 섬, 시드 3개)", loc="left"); ax.set_ylabel("평균 자식 수")
ax.grid(axis="x", visible=False)
fig.tight_layout(); fig.savefig(out / "sim_stage1_wealth.png"); plt.close(fig)

# 5. 근친: 작은 섬 vs 큰 섬
def agg(c):
    kin, dis = [], []
    for s in SEEDS:
        r = load(f"inbreed_{c}_{s}.csv")
        m = sum(int(x["marriages"]) for x in r)
        kin.append(sum(float(x["mean_marriage_kin"]) * int(x["marriages"]) for x in r) / max(1, m))
        b = sum(int(x["births"]) for x in r)
        dis.append(100 * sum(int(x["diseaseA_births"]) for x in r) / max(1, b))
    return kin, dis
ks, ds = agg("small"); kl, dl = agg("large")
fig, axes = plt.subplots(1, 2, figsize=(10, 4))
for ax, (small, large, title, fmt) in zip(axes, [(ks, kl, "혼인한 부부의 평균 혈연 계수", "{:.3f}"), (ds, dl, "유전병 A가 드러난 출생 비율 (%)", "{:.1f}")]):
    xs = [0, 1]
    vals = [st.mean(small), st.mean(large)]
    bars = ax.bar(["작은 섬 (20명)", "큰 섬 (200명)"], vals, color=[BLUE, ORANGE], width=0.55)
    for x, seeds in zip(xs, [small, large]): ax.scatter([x] * 3, seeds, color="#0b0b0b", s=14, zorder=3)
    for b, v, seeds in zip(bars, vals, [small, large]):
        ax.annotate(fmt.format(v), (b.get_x() + b.get_width() / 2, max(seeds)), xytext=(0, 8), textcoords="offset points", ha="center", color=INK2)
    ax.set_ylim(0, max(small + large) * 1.2)
    ax.set_title(title, loc="left"); ax.grid(axis="x", visible=False)
fig.suptitle("근친: 작은 섬은 친척끼리 10배 더 혼인하고 병도 평균적으로 많지만, 시드마다 크게 흔들린다 (300년, 점 = 시드)", x=0.01, ha="left", fontsize=12)
fig.tight_layout(); fig.savefig(out / "sim_stage1_inbreed.png"); plt.close(fig)
print("ok")
