using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace B0r3dQR.Tests
{
    // Tests of the finished one-file exe in bin\: what Windows and a user see.
    static partial class TestMain
    {
        static void NeedTheExe()
        {
            if (!File.Exists(OneExe)) throw new SkipException("bin\\b0r3dQR.exe isn't built (run build.cmd)");
        }

        static void ExeVersion()
        {
            NeedTheExe();
            FileVersionInfo info = FileVersionInfo.GetVersionInfo(OneExe);
            Check(info.ProductVersion == Program.FullVersion, "the exe says " + info.ProductVersion + ", this build says " + Program.FullVersion);
            Check(Regex.IsMatch(Program.Version, @"^\d+\.\d+(\.\d+)?$"), "the version " + Program.Version + " isn't like 0.1 or 0.1.1");
            Check(Program.Build == "dev" || Regex.IsMatch(Program.Build, "^[0-9a-f]{7}$"), "the build stamp " + Program.Build + " is neither dev nor a short commit");
            string[] parts = Program.Version.Split('.');
            string fileVersion = string.Join(".", parts.Concat(Enumerable.Repeat("0", 4 - parts.Length)));
            Check(info.FileVersion == fileVersion, "the file version " + info.FileVersion + " should be " + fileVersion);
            Check(info.ProductName == "b0r3d QR" && info.FileDescription == "b0r3d QR", "the product and description: " + info.ProductName + " / " + info.FileDescription);
            Check(info.CompanyName == "b0r3d.org" && (info.LegalCopyright ?? "").Contains("b0r3d.org"), "the company and copyright: " + info.CompanyName + " / " + info.LegalCopyright);
        }

        // runs a copy of the exe alone in a folder with --selftest (no window), and reads its report
        static void ExeRunsOnItsOwn()
        {
            NeedTheExe();
            string alone = Path.Combine(Work, "alone");
            Fresh(alone);
            string exe = Path.Combine(alone, "b0r3dQR.exe");
            File.Copy(OneExe, exe);
            string report = Path.Combine(alone, "report.txt");
            var start = new ProcessStartInfo(exe, "--selftest \"" + report + "\"") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = alone };
            using (Process run = Process.Start(start))
            {
                if (!run.WaitForExit(30000))
                {
                    run.Kill();
                    Check(false, "--selftest was still running after 30 seconds, so it was stopped");
                }
                Check(File.Exists(report), "--selftest wrote no report (exit code " + run.ExitCode + ")");
                Dictionary<string, string> found = File.ReadAllLines(report).Where(l => l.Contains("="))
                    .ToDictionary(l => l.Substring(0, l.IndexOf('=')), l => l.Substring(l.IndexOf('=') + 1));
                string all = string.Join(", ", found.Select(p => p.Key + "=" + p.Value));
                Check(run.ExitCode == 0 && found["result"] == "ok", "exit code " + run.ExitCode + ": " + all);
                Check(found["read"] == "ok" && found["png"] == "ok" && found["svg"] == "ok", all);
                Check(found["zxing"] == "b0r3dQR", "ZXing came from " + found["zxing"] + ", not from inside the exe");
                Check(found.TryGetValue("windows", out string windows) && windows == "ok", "the windows didn't build from inside the exe: " + all);
                Check(found["dpi"] == "per-monitor-v2", "the exe follows the screens' scaling as \"" + found["dpi"] + "\", not per-monitor-v2 (is the manifest in?)");
                Check(found["version"] == Program.FullVersion, "the exe says " + found["version"]);
            }
            string[] left = Directory.GetFileSystemEntries(alone).Select(Path.GetFileName).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
            Check(left.SequenceEqual(new[] { "b0r3dQR.exe", "report.txt" }), "the self-test left something behind: " + string.Join(", ", left));
        }

        // Reads the exe's metadata without running it, for every way it could go online: networking
        // and web types, XML that can fetch files, pictures and media loaded from addresses, native
        // calls (only user32 and dwmapi are allowed), COM objects (only Windows' Save dialog), and
        // web addresses in its strings (only b0r3d.org's, which opens in the browser).
        static readonly string[] BannedSpaces = { "System.Net", "System.Xml", "System.Windows.Navigation", "System.Web", "System.ServiceModel", "System.Runtime.Remoting" };
        static readonly string[] BannedTypes = { "WebBrowser", "WebClient", "HttpClient", "Socket", "TcpClient", "UdpClient", "BitmapImage", "MediaElement", "MediaPlayer", "XamlReader", "XmlReader", "XmlDocument", "XmlUrlResolver" };
        static readonly string[] AllowedNative = { "user32.dll", "dwmapi.dll" };
        static readonly string[] AllowedCom = { "C0B4E2F3-BA21-4773-8DBA-335EC946EB8B" };  // Windows' Save dialog
        static readonly string[] AllowedAddresses =
        {
            "https://b0r3d.org",                                             // Help → b0r3d.org, opened in the browser
            "pack://application:,,,/b0r3dQR;component/assets/b0r3dQR.ico",   // the cup, inside the exe
            "http://www.w3.org/2000/svg",                                    // SVG's namespace name in saved files, never fetched
        };

        static void ExeNeverGoesOnline()
        {
            NeedTheExe();
            using (FileStream stream = File.OpenRead(OneExe))
            using (var pe = new PEReader(stream))
            {
                MetadataReader md = pe.GetMetadataReader();
                var bad = new List<string>();
                foreach (TypeReferenceHandle handle in md.TypeReferences)
                {
                    TypeReference type = md.GetTypeReference(handle);
                    string space = md.GetString(type.Namespace), name = md.GetString(type.Name);
                    if (BannedSpaces.Any(s => space == s || space.StartsWith(s + ".", StringComparison.Ordinal)) || BannedTypes.Contains(name) || name.Contains("WebRequest") ||
                        space == "System.Windows.Controls" && name == "Frame")
                        bad.Add(space + "." + name);
                }
                foreach (AssemblyReferenceHandle handle in md.AssemblyReferences)
                {
                    string name = md.GetString(md.GetAssemblyReference(handle).Name);
                    if (BannedSpaces.Any(s => name.StartsWith(s, StringComparison.Ordinal))) bad.Add("the assembly " + name);
                }
                foreach (MemberReferenceHandle handle in md.MemberReferences)
                {
                    string name = md.GetString(md.GetMemberReference(handle).Name);
                    if (name == "GetTypeFromProgID" || name == "GetTypeFromCLSID") bad.Add("COM by name (" + name + ")");
                }
                for (int row = 1; row <= md.GetTableRowCount(TableIndex.ModuleRef); row++)
                {
                    string dll = md.GetString(md.GetModuleReference(MetadataTokens.ModuleReferenceHandle(row)).Name).ToLowerInvariant();
                    if (!AllowedNative.Contains(dll)) bad.Add("a native call into " + dll);
                }
                foreach (TypeDefinitionHandle handle in md.TypeDefinitions)
                {
                    TypeDefinition type = md.GetTypeDefinition(handle);
                    bool comClass = (type.Attributes & System.Reflection.TypeAttributes.Import) != 0 && (type.Attributes & System.Reflection.TypeAttributes.Interface) == 0;
                    if (!comClass) continue;
                    string guid = GuidOf(md, type);
                    if (guid == null || !AllowedCom.Contains(guid.ToUpperInvariant())) bad.Add("the COM object " + md.GetString(type.Name) + " {" + guid + "}");
                }
                foreach (string text in OwnStrings(pe, md))
                    foreach (Match address in Regex.Matches(text, @"[A-Za-z][A-Za-z0-9+.\-]*://[^\s""'<>]*"))
                        if (!AllowedAddresses.Contains(address.Value)) bad.Add("the address " + address.Value);
                Check(bad.Count == 0, "the exe has " + string.Join(", ", bad.Distinct()));
                Check(md.TypeReferences.Count > 50, "the scan saw almost nothing; is this the right file?");
                bool zxingInside = md.TypeDefinitions.Any(h => md.GetString(md.GetTypeDefinition(h).Namespace) == "ZXing");
                Check(zxingInside, "ZXing's code isn't inside the exe");
            }
        }

        // The strings b0r3d QR's own code uses (every ldstr in the methods of the B0r3dQR types).
        // ZXing's few addresses (its old project page, the SVG standard's DTD, a map link it can
        // format) are words it writes out, never places it goes; the checks above cover its code.
        static IEnumerable<string> OwnStrings(PEReader pe, MetadataReader md)
        {
            foreach (TypeDefinitionHandle handle in md.TypeDefinitions)
            {
                TypeDefinition type = md.GetTypeDefinition(handle), outer = type;
                while (!outer.GetDeclaringType().IsNil) outer = md.GetTypeDefinition(outer.GetDeclaringType());
                if (!md.GetString(outer.Namespace).StartsWith("B0r3dQR", StringComparison.Ordinal)) continue;
                foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
                {
                    MethodDefinition method = md.GetMethodDefinition(methodHandle);
                    if (method.RelativeVirtualAddress == 0) continue;
                    byte[] il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes();
                    for (int i = 0; i + 4 < il.Length; i++)
                    {
                        if (il[i] != 0x72 || il[i + 4] != 0x70) continue;  // ldstr, then a token into the strings
                        string text = null;
                        try { text = md.GetUserString(MetadataTokens.UserStringHandle(BitConverter.ToInt32(il, i + 1) & 0xFFFFFF)); }
                        catch (BadImageFormatException) { }  // bytes that only looked like an ldstr
                        if (text != null) yield return text;
                    }
                }
            }
        }

        // a type's [Guid("...")], read straight from the metadata
        static string GuidOf(MetadataReader md, TypeDefinition type)
        {
            foreach (CustomAttributeHandle handle in type.GetCustomAttributes())
            {
                CustomAttribute attribute = md.GetCustomAttribute(handle);
                if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;
                MemberReference constructor = md.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
                if (constructor.Parent.Kind != HandleKind.TypeReference) continue;
                if (md.GetString(md.GetTypeReference((TypeReferenceHandle)constructor.Parent).Name) != "GuidAttribute") continue;
                BlobReader value = md.GetBlobReader(attribute.Value);
                value.ReadUInt16();  // the prolog
                return value.ReadSerializedString();
            }
            return null;
        }

        // ILRepack leaves out two of Windows' safety flags; the build puts them back
        static void ExeSafetyFlags()
        {
            NeedTheExe();
            using (FileStream stream = File.OpenRead(OneExe))
            using (var pe = new PEReader(stream))
            {
                DllCharacteristics dll = pe.PEHeaders.PEHeader.DllCharacteristics;
                Check((dll & DllCharacteristics.DynamicBase) != 0, "address randomisation (ASLR) is off");
                Check((dll & DllCharacteristics.NxCompatible) != 0, "data execution prevention (DEP) is off");
                Check((dll & DllCharacteristics.HighEntropyVirtualAddressSpace) != 0, "high-entropy address randomisation is off");
                Check((pe.PEHeaders.CoffHeader.Characteristics & Characteristics.LargeAddressAware) != 0, "large addresses are off");
            }
        }

        // the Save dialog is told not to add saved files to Windows' Recent items (set up, not shown)
        static void SaveDialogStaysOutOfRecent()
        {
            SaveDialog.IFileSaveDialog dialog = SaveDialog.Prepare("Save the code as a PNG picture", "PNG picture", ".png", "b0r3d.org.png");
            try
            {
                dialog.GetOptions(out uint options);
                Check((options & SaveDialog.NotRecent) != 0, "the Save dialog would add files to Recent items (options " + options.ToString("X") + ")");
                Check((options & SaveDialog.Options) == SaveDialog.Options, "the Save dialog lost some of its options (" + options.ToString("X") + ")");
                dialog.GetFileName(out string name);
                Check(name == "b0r3d.org.png", "the Save dialog's file name is " + name);
            }
            finally
            {
                Marshal.ReleaseComObject(dialog);
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);

        [DllImport("kernel32.dll")]
        static extern bool FreeLibrary(IntPtr module);

        [DllImport("kernel32.dll")]
        static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);

        [DllImport("kernel32.dll")]
        static extern IntPtr LoadResource(IntPtr module, IntPtr info);

        [DllImport("kernel32.dll")]
        static extern IntPtr LockResource(IntPtr data);

        [DllImport("kernel32.dll")]
        static extern uint SizeofResource(IntPtr module, IntPtr info);

        delegate bool EnumNames(IntPtr module, IntPtr type, IntPtr name, IntPtr param);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern bool EnumResourceNames(IntPtr module, IntPtr type, EnumNames callback, IntPtr param);

        [DllImport("user32.dll")]
        static extern IntPtr CreateIconFromResourceEx(byte[] bits, uint size, bool icon, uint version, int width, int height, uint flags);

        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr icon);

        static readonly IntPtr RtIcon = (IntPtr)3, RtGroupIcon = (IntPtr)14;

        static byte[] ResourceBytes(IntPtr module, IntPtr name, IntPtr type)
        {
            IntPtr info = FindResource(module, name, type);
            if (info == IntPtr.Zero) return null;
            var bytes = new byte[SizeofResource(module, info)];
            Marshal.Copy(LockResource(LoadResource(module, info)), bytes, 0, bytes.Length);
            return bytes;
        }

        // The exe's own icon, as Windows reads it: every size there, small ones as bitmaps (not PNG),
        // and each one a picture of the cup rather than static.
        static void ExeIcon()
        {
            NeedTheExe();
            IntPtr module = LoadLibraryEx(OneExe, IntPtr.Zero, 0x2 | 0x20);  // as a data file, for its resources
            Check(module != IntPtr.Zero, "couldn't open the exe's resources");
            try
            {
                IntPtr group = IntPtr.Zero;
                EnumResourceNames(module, RtGroupIcon, (m, t, name, p) => { group = name; return false; }, IntPtr.Zero);
                Check(group != IntPtr.Zero, "the exe has no icon");
                byte[] dir = ResourceBytes(module, group, RtGroupIcon);
                int count = BitConverter.ToUInt16(dir, 4);
                var sizes = new List<int>();
                for (int i = 0; i < count; i++)
                {
                    int at = 6 + 14 * i;
                    int size = dir[at] == 0 ? 256 : dir[at];
                    sizes.Add(size);
                    byte[] frame = ResourceBytes(module, (IntPtr)BitConverter.ToUInt16(dir, at + 12), RtIcon);
                    Check(frame != null, size + " px is missing");
                    bool png = frame[0] == 0x89 && frame[1] == (byte)'P';
                    Check(png == (size >= 256), size + " px is stored as " + (png ? "a PNG" : "a bitmap"));
                    IntPtr icon = CreateIconFromResourceEx(frame, (uint)frame.Length, true, 0x00030000, size, size, 0);
                    Check(icon != IntPtr.Zero, "Windows couldn't read the " + size + " px picture");
                    try
                    {
                        BitmapSource picture = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        string why = WhyNotAPicture(picture);
                        Check(why == null, size + " px: " + why);
                    }
                    finally
                    {
                        DestroyIcon(icon);
                    }
                }
                sizes.Sort();
                Check(sizes.SequenceEqual(new[] { 16, 20, 24, 32, 40, 48, 64, 256 }), "the icon's sizes are " + string.Join(", ", sizes));
            }
            finally
            {
                FreeLibrary(module);
            }
        }

        static void StaticIsCaught()
        {
            var random = new Random(12345);  // the same "random" static every run
            foreach (int size in new[] { 16, 32 })
            {
                var pixels = new byte[size * size * 4];
                random.NextBytes(pixels);
                BitmapSource noise = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
                Check(WhyNotAPicture(noise) != null, size + " px of static passed as a picture");
            }
        }

        // .NET Framework draws an icon's PNG-packed pictures under 256 px as random static (LoafLock
        // saw it with b0r3d.org's rukus.ico). Static is see-through in random places, mostly half
        // see-through, and no two neighbouring pixels look alike; the cup is the opposite. Returns
        // why the picture looks like static, or null when it doesn't.
        static string WhyNotAPicture(BitmapSource picture)
        {
            var bgra = new FormatConvertedBitmap(picture, PixelFormats.Bgra32, null, 0);
            int w = bgra.PixelWidth, h = bgra.PixelHeight;
            var px = new byte[w * h * 4];
            bgra.CopyPixels(px, w * 4, 0);
            int Alpha(int x, int y) => px[(y * w + x) * 4 + 3];
            if (Alpha(0, 0) > 8 || Alpha(w - 1, 0) > 8 || Alpha(0, h - 1) > 8 || Alpha(w - 1, h - 1) > 8) return "the corners aren't see-through";
            int crisp = 0, pairs = 0;
            long diff = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 4;
                    if (px[i + 3] <= 8 || px[i + 3] >= 247) crisp++;
                    if (x + 1 < w && px[i + 3] >= 247 && px[i + 7] >= 247)
                    {
                        pairs++;
                        diff += Math.Abs(px[i] - px[i + 4]) + Math.Abs(px[i + 1] - px[i + 5]) + Math.Abs(px[i + 2] - px[i + 6]);
                    }
                }
            if (crisp * 10 < w * h * 6) return "only " + (100 * crisp / (w * h)) + "% of the pixels are solid or clear";
            if (pairs == 0) return "no solid pixels side by side";
            if (diff / pairs > 120) return "side-by-side pixels differ by " + (diff / pairs) + " on average";
            return null;
        }

        static void BuildCmdIsPlain()
        {
            string file = Path.Combine(RepoFolder, "build.cmd");
            if (!File.Exists(file)) throw new SkipException("not run from the repo");
            byte[] bytes = File.ReadAllBytes(file);
            Check(bytes.All(b => b < 128), "build.cmd has a character that isn't plain ASCII");
            for (int i = 0; i < bytes.Length; i++)
                if (bytes[i] == 10) Check(i > 0 && bytes[i - 1] == 13, "build.cmd has a line ending without CR (byte " + i + ")");
        }
    }
}
