"""CAD Master setup test: proves that Python, pywin32, the solidworks-api skill's swlib and this SOLIDWORKS
work together, the way Claude's build scripts use them.

Builds a 40 x 20 x 10 mm block in a new, unsaved part, checks it (1 body, 8000 mm3) and closes it.
Nothing else is opened, changed or closed.

    python cadmaster_selftest.py <folder with swlib.py>

CAD Master runs it with CADMASTER_SW_PID set, so swlib attaches to the SOLIDWORKS hosting the panel.
Prints one JSON line: {"ok": true, "solidworks": "33.1.2", "bodies": 1, "volume": 8000.0, "seconds": 12.3}
"""
import json
import sys
import time


def main():
    t0 = time.time()
    out = {"ok": False}
    sw = None
    try:
        sys.path.insert(0, sys.argv[1])
        import swlib
        out["swlib"] = swlib.__file__
        sw = swlib.SwApp()
        out["solidworks"] = sw.app.RevisionNumber()
        p = swlib.Part(sw, "CAD Master setup test")
        p.sketch("z", 0, "TEST_SKETCH")
        p.rect(-20, -10, 20, 10)
        p.extrude("TEST_BLOCK", 10)
        bodies, volume = len(p.bodies()), p.volume()
        out.update(bodies=bodies, volume=round(volume, 1))
        out["ok"] = bodies == 1 and abs(volume - 8000.0) < 1.0
        if not out["ok"]:
            out["error"] = "expected 1 body of 8000 mm3, got %d of %.1f mm3" % (bodies, volume)
    except Exception as e:  # report every failure as JSON for the panel
        out["error"] = "%s: %s" % (type(e).__name__, e)
    finally:
        if sw is not None:
            sw.close_created()
    out["seconds"] = round(time.time() - t0, 1)
    print(json.dumps(out))


if __name__ == "__main__":
    main()
