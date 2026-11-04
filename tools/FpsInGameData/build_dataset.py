"""
Gera a base embutida da categoria Fps-In-Game do Pulse1x.

Fontes (nenhuma estimativa é feita aqui, só conversão de formato):
  * PCGameBenchmarks — "PC Game System Requirements & Estimated FPS by GPU" (CC BY 4.0)
    https://huggingface.co/datasets/pcgamebenchmarks/pc-game-system-requirements-fps
    games.csv: requisitos da Steam + FPS estimado (1080p High) em 12 GPUs de referência.
    gpus.csv:  score PassMark G3D Mark de ~1.400 GPUs.
  * SteamSpy (https://steamspy.com/api.php) — só para ordenar os jogos por popularidade.

Uso (a partir da raiz do repositório):
    py -I tools/FpsInGameData/build_dataset.py

Saída (recursos embutidos no executável):
    src/Pulse1x.App/Resources/FpsInGame/games.tsv.gz
    src/Pulse1x.App/Resources/FpsInGame/gpus.tsv.gz
"""

import csv
import gzip
import io
import json
import os
import sys
import urllib.request

HF = "https://huggingface.co/datasets/pcgamebenchmarks/pc-game-system-requirements-fps/resolve/main/"
STEAMSPY = "https://steamspy.com/api.php?request="

# Colunas de FPS do games.csv e o nome da GPU correspondente no gpus.csv.
REFERENCE_GPUS = [
    ("fps_1080p_high_iris_xe", "Intel Iris Xe"),
    ("fps_1080p_high_radeon_780m", "Radeon 780M"),
    ("fps_1080p_high_gtx_1650", "GeForce GTX 1650"),
    ("fps_1080p_high_gtx_1660_super", "GeForce GTX 1660 SUPER"),
    ("fps_1080p_high_rtx_3060", "GeForce RTX 3060"),
    ("fps_1080p_high_rx_7600", "Radeon RX 7600"),
    ("fps_1080p_high_rtx_4060", "GeForce RTX 4060"),
    ("fps_1080p_high_rtx_5060", "GeForce RTX 5060"),
    ("fps_1080p_high_rtx_4070", "GeForce RTX 4070"),
    ("fps_1080p_high_rx_7800_xt", "Radeon RX 7800 XT"),
    ("fps_1080p_high_rtx_5070", "GeForce RTX 5070"),
    ("fps_1080p_high_rtx_4090", "GeForce RTX 4090"),
]


def fetch(url: str) -> bytes:
    req = urllib.request.Request(url, headers={"User-Agent": "Pulse1x-dataset-builder"})
    with urllib.request.urlopen(req, timeout=120) as r:
        return r.read()


def clean(value) -> str:
    """Campos de texto viram uma linha só, sem tabulação (o separador do TSV)."""
    if value is None:
        return ""
    return " ".join(str(value).replace("\t", " ").split())


def popularity_ranks() -> dict:
    """AppId -> posição (0 = mais popular), pelo total de avaliações na Steam."""
    scores = {}
    for request in ("all&page=0", "all&page=1", "top100in2weeks", "top100forever"):
        try:
            data = json.loads(fetch(STEAMSPY + request))
        except Exception as ex:  # SteamSpy limita "all" a 1 chamada/min; seguimos com o que vier
            print(f"  aviso: SteamSpy {request} falhou ({ex})", file=sys.stderr)
            continue
        for appid, info in data.items():
            reviews = int(info.get("positive") or 0) + int(info.get("negative") or 0)
            # Jogos em alta nas últimas 2 semanas ganham um empurrão para aparecer no topo.
            if request == "top100in2weeks":
                reviews = int(reviews * 1.5) + 1
            scores[appid] = max(scores.get(appid, 0), reviews)
    ordered = sorted(scores.items(), key=lambda kv: kv[1], reverse=True)
    return {appid: i for i, (appid, _) in enumerate(ordered)}


def main() -> None:
    root = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
    out_dir = os.path.join(root, "src", "Pulse1x.App", "Resources", "FpsInGame")
    os.makedirs(out_dir, exist_ok=True)

    print("Baixando gpus.csv ...")
    gpus = list(csv.DictReader(io.StringIO(fetch(HF + "gpus.csv").decode("utf-8"))))
    gpu_score = {g["name"]: int(g["g3d_mark"]) for g in gpus if g["g3d_mark"]}

    print("Baixando games.csv ...")
    games = list(csv.DictReader(io.StringIO(fetch(HF + "games.csv").decode("utf-8"))))

    print("Consultando popularidade na SteamSpy ...")
    ranks = popularity_ranks()

    # ---- gpus.tsv: nome, tipo, g3d ----
    lines = ["#name\ttype\tg3d"]
    for g in gpus:
        if g["g3d_mark"]:
            lines.append(f"{clean(g['name'])}\t{clean(g['type'])}\t{int(g['g3d_mark'])}")
    write_gz(os.path.join(out_dir, "gpus.tsv.gz"), lines)

    # ---- games.tsv ----
    # Primeira linha: scores G3D das GPUs de referência, na ordem das colunas de FPS.
    ref_scores = [gpu_score[name] for _, name in REFERENCE_GPUS]
    lines = ["#ref\t" + "\t".join(str(s) for s in ref_scores)]
    lines.append("#name\tyear\tappid\tmin_gpu\tmin_cpu\tmin_ram\trec_gpu\trec_cpu\trec_ram"
                 "\tmin_g3d\trec_g3d\tfps_cap\tpop\tslug\tfps(12, separados por ';')")
    kept = 0
    for g in games:
        fps = [g[col] for col, _ in REFERENCE_GPUS]
        if not all(fps):
            continue  # sem estimativa não há o que mostrar
        appid = g["steam_appid"] or ""
        slug = (g.get("page_url") or "").rsplit("/game/", 1)[-1]
        row = [
            clean(g["name"]), g["release_year"] or "", appid,
            clean(g["min_gpu"]), clean(g["min_cpu"]), g["min_ram_gb"] or "",
            clean(g["rec_gpu"]), clean(g["rec_cpu"]), g["rec_ram_gb"] or "",
            g["min_gpu_g3d"] or "", g["rec_gpu_g3d"] or "", g["engine_fps_cap"] or "",
            str(ranks.get(appid, "")), clean(slug), ";".join(fps),
        ]
        lines.append("\t".join(row))
        kept += 1
    write_gz(os.path.join(out_dir, "games.tsv.gz"), lines)

    print(f"OK: {kept} jogos, {len(gpus)} GPUs, {len(ranks)} jogos com popularidade -> {out_dir}")


def write_gz(path: str, lines: list) -> None:
    with gzip.open(path, "wb", compresslevel=9) as f:
        f.write(("\n".join(lines) + "\n").encode("utf-8"))
    print(f"  {os.path.basename(path)}: {os.path.getsize(path) // 1024} KB")


if __name__ == "__main__":
    main()
