"""Create the in-game verification fixture save for the HDBF automated rounds.

The fixture is a *copy* of a real settlement save under Saves\\<settlement>, with two entities
injected and the whole map set to maximum soil moisture. It is the reproducible test bed used by
tools/dev/Invoke-GameRound.ps1: the game autoloads it with -saveName/-settlementName and the mod's
temporary diagnostics report what the managed-plot scan sees.

Why injection instead of UI placement: the automated rounds cannot rely on synthetic mouse input
on this machine (see docs/NEXT_ROUND.md round-7 notes), so the farm is placed by editing the save's
world.json. The injected entity carries the vanilla persisted component set that the game needs to
load a building (PausableBuilding / Workplace / BlockObject / WorkplaceWorkerType / Emptiable);
without them `WorkplaceWorkerType.Load` throws ArgumentOutOfRangeException (VERIFICATION §14).

Coordinates are map-specific: on the pinned 110000 map (153x153-ish) the flat, plantable area used
so far is the 11x11 window centred on (130,140) at surface height 3, with one Path entity next to
the entrance. Re-derive the coordinates with find-farm-spot style probes if the source map changes;
TerrainMap.Voxels column height must equal Coordinates.Z for a valid placement.

Usage (PowerShell):
  python tools\\dev\\fixtures\\craft-farm-fixture.py
  python tools\\dev\\fixtures\\craft-farm-fixture.py --all-moist false --out Saves\\HDBFTest
"""

import argparse
import json
import os
import shutil
import sys
import uuid
import zipfile

DEFAULT_SOURCE = os.path.join(
    os.path.expanduser("~"),
    "Documents", "Timberborn", "Saves", "110000",
    "2026-10-24 18h28m, Day 1-1.autosave.timber",
)
DEFAULT_OUT = os.path.join(
    os.path.expanduser("~"), "Documents", "Timberborn", "Saves", "HDBFTest"
)
# The 110000 map has a real road chain at y=139, x=130..136 and the district centre at (131,138).
# A farm anchored at (129,140) occupies x=129..131 / y=140..141 and its entrance lands on the
# existing road cell (130,139) (anchor + (1,-1) = entrance), so vanilla navigation/flow fields and
# the district road connection are real instead of injected. The plot window stays inside the flat
# grass area and avoids the district centre / Inventor building.
DEFAULT_FARM = (129, 140, 3)
DEFAULT_PATH = None            # no extra Path entity: the entrance cell is already a real road
MAX_MOISTURE = 16


def load_world(path):
    with zipfile.ZipFile(path) as archive:
        world = json.loads(archive.read("world.json").decode("utf-8"))
        infos = archive.infolist()
        blobs = {info.filename: archive.read(info.filename) for info in infos}
    return world, infos, blobs


def inject_entities(world, farm, path, tier="BoxFarm.Basic.Folktails"):
    entities = world.setdefault("Entities", [])
    if path is not None:
        entities.append({
            "Id": str(uuid.uuid4()),
            "Template": "Path",
            "Components": {
                "BlockObject": {
                    "Coordinates": {"X": path[0], "Y": path[1], "Z": path[2]},
                    "Orientation": "Cw270",
                }
            },
        })
    entities.append({
        "Id": str(uuid.uuid4()),
        "Template": tier,
        "Components": {
            "PausableBuilding": {"Paused": False},
            "Workplace": {"DesiredWorkers": 2},
            "BlockObject": {
                "Coordinates": {"X": farm[0], "Y": farm[1], "Z": farm[2]},
                "Orientation": "Cw0",
            },
            "WorkplaceWorkerType": {"WorkerType": "Beaver", "WasSet": True},
            "Emptiable": {"IsMarkedForEmptying": False, "StatusIsActive": False},
        },
    })
    return entities


def aim_camera(world, farm, zoom, hangle, vangle):
    """Point the settlement camera at the injected farm (for screenshot rounds)."""
    camera = world.get("Singletons", {}).get("CameraService", {}).get("CameraState")
    if not camera:
        return False
    camera["Target"] = {"X": float(farm[0]) + 1.5, "Y": float(farm[1]), "Z": float(farm[2]) + 1.0}
    if zoom is not None:
        camera["ZoomLevel"] = float(zoom)
    if hangle is not None:
        camera["HorizontalAngle"] = float(hangle)
    if vangle is not None:
        camera["VerticalAngle"] = float(vangle)
    return True


def set_all_moist(world, level):
    simulator = world.get("Singletons", {}).get("SoilMoistureSimulator")
    if not simulator or "MoistureLevels" not in simulator:
        raise SystemExit("source save has no SoilMoistureSimulator.MoistureLevels")
    levels = simulator["MoistureLevels"]
    if isinstance(levels, dict) and "Array" in levels:
        levels["Array"] = " ".join(str(level) for _ in levels["Array"].split())
    elif isinstance(levels, str):
        simulator["MoistureLevels"] = " ".join(str(level) for _ in levels.split())
    else:
        raise SystemExit("unexpected MoistureLevels shape: %r" % (type(levels),))


def write_save(world, infos, blobs, source, out_dir, file_name):
    if os.path.isdir(out_dir):
        shutil.rmtree(out_dir)
    os.makedirs(out_dir)
    out_path = os.path.join(out_dir, file_name)
    payload = json.dumps(world, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    with zipfile.ZipFile(out_path, "w", zipfile.ZIP_DEFLATED) as archive:
        for info in infos:
            data = payload if info.filename == "world.json" else blobs[info.filename]
            archive.writestr(info, data)
    return out_path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", default=DEFAULT_SOURCE)
    parser.add_argument("--out", default=DEFAULT_OUT)
    parser.add_argument("--all-moist", default="true")
    parser.add_argument("--moisture", type=int, default=MAX_MOISTURE)
    parser.add_argument("--farm", default="%d,%d,%d" % DEFAULT_FARM)
    parser.add_argument("--camera-zoom", default="0.9")
    parser.add_argument("--camera-hangle", default="205")
    parser.add_argument("--camera-vangle", default="40")
    parser.add_argument("--path", default="")
    parser.add_argument("--tier", default="BoxFarm.Basic.Folktails")
    args = parser.parse_args()

    if not os.path.isfile(args.source):
        raise SystemExit("source save not found: %s" % args.source)
    farm = tuple(int(part) for part in args.farm.split(","))
    path = tuple(int(part) for part in args.path.split(",")) if args.path.strip() else None

    world, infos, blobs = load_world(args.source)
    inject_entities(world, farm, path, args.tier)
    if aim_camera(world, farm, args.camera_zoom, args.camera_hangle, args.camera_vangle):
        print("  camera aimed at farm %s" % (farm,))
    if args.all_moist.lower() == "true":
        set_all_moist(world, args.moisture)

    file_name = os.path.basename(args.source)
    out_path = write_save(world, infos, blobs, args.source, args.out, file_name)
    print("fixture ready: %s (%d bytes)" % (out_path, os.path.getsize(out_path)))
    print("  farm=%s path=%s tier=%s all_moist=%s" % (farm, path, args.tier, args.all_moist))
    print("  autoload with: -saveName \"%s\" -settlementName \"%s\""
          % (os.path.splitext(file_name)[0], os.path.basename(args.out)))


if __name__ == "__main__":
    sys.exit(main())
