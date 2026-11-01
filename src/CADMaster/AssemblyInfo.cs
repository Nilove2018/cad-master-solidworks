using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("CAD Master")]
[assembly: AssemblyDescription("Claude Code inside SOLIDWORKS")]
[assembly: AssemblyProduct("CAD Master")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Nilove Mandal")]
// Keep AssemblyVersion fixed: the COM registration names it ("CADMaster, Version=1.0.0.0"), and every
// installed version must match that registration. The product version (VERSION file) goes into
// AssemblyFileVersion and AssemblyInformationalVersion, which build.ps1 generates in build\BuildInfo.cs.
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: ComVisible(false)]
