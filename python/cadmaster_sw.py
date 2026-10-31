"""Attach to the SOLIDWORKS that hosts the CAD Master panel.

CAD Master starts Claude Code with CADMASTER_SW_PID set and this folder on PYTHONPATH.
Several SOLIDWORKS processes can run at once (for example a hidden "-Embedding" one), and
GetActiveObject may return any of them, so attach by process ID through the running object
table, where every SOLIDWORKS registers as "SolidWorks_PID_<pid>".

    import cadmaster_sw
    raw = cadmaster_sw.get_raw()          # for swlib: swlib.cast(raw, "ISldWorks")
    app = cadmaster_sw.get_app()          # late-bound Dispatch, for quick scripts

swlib.SwApp() does this by itself when CADMASTER_SW_PID is set (swlib 2.2+).
"""
import os

import pythoncom
import win32com.client


def running_pids():
    """Process IDs of every SOLIDWORKS registered in the running object table."""
    rot = pythoncom.GetRunningObjectTable()
    ctx = pythoncom.CreateBindCtx(0)
    pids = []
    for moniker in rot.EnumRunning():
        try:
            name = moniker.GetDisplayName(ctx, None)
        except pythoncom.com_error:
            continue
        if name.startswith("SolidWorks_PID_"):
            pids.append(int(name.rsplit("_", 1)[1]))
    return pids


def get_raw(pid=None):
    """The COM object (PyIUnknown) of SOLIDWORKS process `pid` (default: CADMASTER_SW_PID)."""
    pid = int(pid or os.environ.get("CADMASTER_SW_PID") or 0)
    rot = pythoncom.GetRunningObjectTable()
    ctx = pythoncom.CreateBindCtx(0)
    for moniker in rot.EnumRunning():
        try:
            name = moniker.GetDisplayName(ctx, None)
        except pythoncom.com_error:
            continue
        if name.startswith("SolidWorks_PID_") and (pid == 0 or name == "SolidWorks_PID_%d" % pid):
            return rot.GetObject(moniker)
    raise RuntimeError("SOLIDWORKS process %s is not in the running object table (running: %s)" % (pid or "any", running_pids()))


def get_app(pid=None):
    """Late-bound ISldWorks of the CAD Master SOLIDWORKS. Late binding turns zero-argument methods
    into properties (app.RevisionNumber, not app.RevisionNumber()); see solidworks-api skill section 3.0.
    For builds prefer swlib (early-bound)."""
    return win32com.client.Dispatch(get_raw(pid).QueryInterface(pythoncom.IID_IDispatch))


if __name__ == "__main__":
    app = get_app()
    print("Attached to SOLIDWORKS", app.RevisionNumber, "PID", os.environ.get("CADMASTER_SW_PID", "(first found)"))
