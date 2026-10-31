using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using SolidWorks.Interop.sldworks;

namespace CADMaster
{
    /// <summary>Finds a running SOLIDWORKS in the COM running object table by its "SolidWorks_PID_&lt;pid&gt;" moniker.</summary>
    public static class SwRot
    {
        public static ISldWorks Connect(int pid, out int foundPid)
        {
            foundPid = 0;
            GetRunningObjectTable(0, out IRunningObjectTable rot);
            CreateBindCtx(0, out IBindCtx ctx);
            rot.EnumRunning(out IEnumMoniker monikers);
            var one = new IMoniker[1];
            while (monikers.Next(1, one, IntPtr.Zero) == 0)
            {
                string name;
                try { one[0].GetDisplayName(ctx, null, out name); } catch { continue; }
                if (!name.StartsWith("SolidWorks_PID_")) continue;
                if (!int.TryParse(name.Substring("SolidWorks_PID_".Length), out int p)) continue;
                if (pid != 0 && p != pid) continue;
                rot.GetObject(one[0], out object obj);
                if (obj is ISldWorks sw) { foundPid = p; return sw; }
            }
            return null;
        }

        [DllImport("ole32.dll")] static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable rot);
        [DllImport("ole32.dll")] static extern int CreateBindCtx(int reserved, out IBindCtx ctx);
    }
}
