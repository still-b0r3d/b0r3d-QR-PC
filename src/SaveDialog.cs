using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace B0r3dQR
{
    // Windows' own Save dialog, called directly instead of through WPF, because WPF on .NET
    // Framework can't tell it not to add the saved file to Windows' Recent items (kept in AppData
    // and the registry). Windows still remembers the last folder the dialog was in for b0r3d QR;
    // that memory is Windows' own.
    static class SaveDialog
    {
        const uint OverwritePrompt = 0x2, StrictFileTypes = 0x4, ForceFileSystem = 0x40, PathMustExist = 0x800,
            NoReadOnlyReturn = 0x8000, DontAddToRecent = 0x2000000;

        // asked of every Save dialog: Windows' own "replace it?" question, the file type's
        // extension kept, a real file, never a read-only one, and not added to Recent items
        internal const uint Options = OverwritePrompt | StrictFileTypes | ForceFileSystem | PathMustExist | NoReadOnlyReturn | DontAddToRecent;
        internal const uint NotRecent = DontAddToRecent;

        const int Cancelled = unchecked((int)0x800704C7);  // the user clicked Cancel
        const uint FileSystemPath = 0x80058000;

        // the chosen file, or null when cancelled
        public static string Ask(Window owner, string title, string typeName, string extension, string fileName)
        {
            IFileSaveDialog dialog = Prepare(title, typeName, extension, fileName);
            try
            {
                IntPtr parent = owner == null ? IntPtr.Zero : new WindowInteropHelper(owner).Handle;
                int result = dialog.Show(parent);
                if (result == Cancelled) return null;
                if (result < 0) Marshal.ThrowExceptionForHR(result);
                dialog.GetResult(out IShellItem item);
                try
                {
                    item.GetDisplayName(FileSystemPath, out IntPtr name);
                    try { return Marshal.PtrToStringUni(name); }
                    finally { Marshal.FreeCoTaskMem(name); }
                }
                finally
                {
                    Marshal.ReleaseComObject(item);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(dialog);
            }
        }

        // the dialog, set up but not shown (the tests check its options this way)
        internal static IFileSaveDialog Prepare(string title, string typeName, string extension, string fileName)
        {
            var dialog = (IFileSaveDialog)new FileSaveDialogObject();
            dialog.GetOptions(out uint options);
            dialog.SetOptions(options | Options);
            dialog.SetTitle(title);
            dialog.SetFileTypes(1, new[] { new FilterSpec { Name = typeName + " (*" + extension + ")", Spec = "*" + extension } });
            dialog.SetDefaultExtension(extension.TrimStart('.'));
            dialog.SetFileName(fileName);
            return dialog;
        }

        [ComImport, Guid("C0B4E2F3-BA21-4773-8DBA-335EC946EB8B")]
        class FileSaveDialogObject
        {
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct FilterSpec
        {
            [MarshalAs(UnmanagedType.LPWStr)] public string Name;
            [MarshalAs(UnmanagedType.LPWStr)] public string Spec;
        }

        // IFileSaveDialog, with IModalWindow's and IFileDialog's methods first, in Windows' order
        [ComImport, Guid("84BCCD23-5FDE-4CDB-AEA4-AF64B83D78AB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IFileSaveDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint count, [MarshalAs(UnmanagedType.LPArray)] FilterSpec[] filters);
            void SetFileTypeIndex(uint index);
            void GetFileTypeIndex(out uint index);
            void Advise(IntPtr events, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint options);
            void GetOptions(out uint options);
            void SetDefaultFolder(IShellItem item);
            void SetFolder(IShellItem item);
            void GetFolder(out IShellItem item);
            void GetCurrentSelection(out IShellItem item);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void GetResult(out IShellItem item);
            void AddPlace(IShellItem item, int place);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
            void Close(int result);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr filter);
            void SetSaveAsItem(IShellItem item);
            void SetProperties(IntPtr store);
            void SetCollectedProperties(IntPtr list, int appendDefault);
            void GetProperties(out IntPtr store);
            void ApplyProperties(IShellItem item, IntPtr store, IntPtr window, IntPtr sink);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IShellItem
        {
            void BindToHandler(IntPtr context, ref Guid handler, ref Guid interfaceId, out IntPtr result);
            void GetParent(out IShellItem parent);
            void GetDisplayName(uint form, out IntPtr name);
            void GetAttributes(uint mask, out uint attributes);
            void Compare(IShellItem other, uint hint, out int order);
        }
    }
}
