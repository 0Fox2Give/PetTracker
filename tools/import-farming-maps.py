"""Rebuild the offline map bundle from a pinned upstream revision.

Only NPCs in our audited regional soul-source list are admitted. Coordinates
use upstream's approximate calibration; they are not live player positions.
"""
import concurrent.futures
import hashlib
import json
from pathlib import Path
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
REVISION = "e16d35d4f19e19b03b0cb05ab4a1658bb51102a6"
SOURCE = "https://raw.githubusercontent.com/SkeeveAN/Aion-DPS-Meter/" + REVISION + "/"
OUT = ROOT / "assets" / "maps"


def fetch(path):
    with urllib.request.urlopen(SOURCE + path, timeout=60) as response:
        return response.read()


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    hashes = {}

    def source_json(path):
        data = fetch(path)
        hashes[path] = hashlib.sha256(data).hexdigest()
        return json.loads(data)

    maps = source_json("Client/assets/aion2/maps/maps.json")["maps"]
    spawns = source_json("Client/assets/aion2/pets/spawns.json")["maps"]
    with (ROOT / "src/Core/FarmingLinks.json").open(encoding="utf-8-sig") as file:
        links = json.load(file)["Rows"]
    with urllib.request.urlopen("https://api.github.com/repos/SkeeveAN/Aion-DPS-Meter/git/trees/" + REVISION + "?recursive=1", timeout=60) as response:
        tree = json.load(response)["tree"]
    regions = []
    jobs = []
    for key in ("verteron", "altgard"):
        m = maps[key]
        size = m["tile"] * m["grid"]
        points = set()
        for pet in links:
            for mob in pet.get("SoulSources") or []:
                if mob["Region"] != m["title"]:
                    continue
                for x, y, *_ in spawns.get(m["table"], {}).get(str(mob["NpcId"]), []):
                    nx = (m["offsetX"] + m["scale"] * x) / size
                    ny = (m["offsetY"] + m["scale"] * y) / size
                    if 0 <= nx <= 1 and 0 <= ny <= 1:
                        points.add((pet["PetId"], mob["NpcId"], mob["Name"], round(nx, 7), round(ny, 7)))
        tiles = []
        prefix = "Client/assets/aion2/maps/" + key + "/"
        for item in tree:
            path = item["path"]
            if path.startswith(prefix) and path.endswith(".jpg"):
                filename = path.removeprefix(prefix)
                col, row = map(int, filename[:-4].split("_"))
                tiles.append({"Column": col, "Row": row})
                jobs.append((path, key + "/" + filename))
        if len(tiles) != m["tiles"] or not points:
            raise ValueError(f"Incomplete region data: {key}")
        regions.append({"Key": key, "Name": m["title"], "Grid": m["grid"], "Tiles": sorted(tiles, key=lambda t: (t["Column"], t["Row"])),
                        "Points": [dict(zip(("PetId", "NpcId", "Name", "X", "Y"), p)) for p in sorted(points)]})

    def tile(job):
        path, local = job
        data = fetch(path)
        target = OUT / local
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)
        return path, hashlib.sha256(data).hexdigest()

    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        hashes.update(pool.map(tile, jobs))
    license_data = fetch("LICENSE")
    (ROOT / "licenses/Aion-DPS-Meter-MIT.txt").write_bytes(license_data)
    hashes["LICENSE"] = hashlib.sha256(license_data).hexdigest()
    (OUT / "provenance.json").write_text(json.dumps({"Repository": "https://github.com/SkeeveAN/Aion-DPS-Meter", "Revision": REVISION, "Calibration": "Approximate upstream manual fit. No live player positioning.", "Sha256": dict(sorted(hashes.items()))}, indent=2) + "\n", encoding="utf-8", newline="\n")
    (ROOT / "src/Core/FarmingSpawns.json").write_text(json.dumps(regions, separators=(",", ":")) + "\n", encoding="utf-8", newline="\n")
    print(f"Imported {len(jobs)} tiles and {sum(len(m['Points']) for m in regions)} audited spawn points.")


if __name__ == "__main__":
    main()
