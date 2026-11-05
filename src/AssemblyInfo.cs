using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// the tests exe reaches inside: its own data folder, a pretend screen scaling, the window's parts
[assembly: InternalsVisibleTo("b0r3dQRTests")]

// Every native call loads its Windows DLL from System32 only. Without this, .NET Framework looks
// beside the exe first, so a DLL of the same name sitting next to b0r3dQR.exe (in Downloads, say)
// would run in its place: Media Foundation's, which Windows doesn't protect as it does user32's.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
