using System;
using System.IO;
using System.Runtime.InteropServices;

namespace GitPort {
    static class Program {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

        [DllImport("shcore.dll", SetLastError = true)]
        private static extern int SetProcessDpiAwareness(int awareness);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDPIAware();

        [DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true)]
        private static extern int SetPreferredAppMode(int appMode);

        private static void EnableHighDpi() {
            try {
                // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4
                if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return;
            } catch {}
            try {
                // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE = -3
                if (SetProcessDpiAwarenessContext(new IntPtr(-3))) return;
            } catch {}
            try {
                // PROCESS_PER_MONITOR_DPI_AWARE = 2
                if (SetProcessDpiAwareness(2) == 0) return;
            } catch {}
            try {
                SetProcessDPIAware();
            } catch {}
        }

        private static void EnableDarkMode() {
            try {
                SetPreferredAppMode(1); // 1 = AllowDark
            } catch {}
        }

        [STAThread]
        static int Main(string[] args) {
            EnableHighDpi();
            EnableDarkMode();
            if (args.Length > 0 && args[0] == "--test-echo") {
                using (var stdout = new StreamWriter(Console.OpenStandardOutput(), new System.Text.UTF8Encoding(false))) {
                    stdout.Write(args.Length > 1 ? args[1] : "OK");
                    stdout.Flush();
                }
                return 0;
            }
            if (args.Length > 0 && args[0] == "--test-wait") {
                System.Threading.Thread.Sleep(5000);
                return 0;
            }
            try {
                string title = args.Length > 0 ? args[0] : "Select Git repository";
                string initialPath = args.Length > 1 ? args[1] : "";
                string result = SelectFolder(title, initialPath);
                if (!string.IsNullOrEmpty(result)) {
                    using (var stdout = new StreamWriter(Console.OpenStandardOutput(), new System.Text.UTF8Encoding(false))) {
                        stdout.Write(result);
                        stdout.Flush();
                    }
                    return 0;
                }
                return 1;
            } catch {
                return 2;
            }
        }

        [ComImport]
        [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem {
            void BindToHandler();
            void GetParent();
            void GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
            void GetAttributes();
            void Compare();
        }

        [ComImport]
        [Guid("D57C7288-D4AD-4768-BE02-9D969532D960")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes();
            void SetFileTypeIndex();
            void GetFileTypeIndex();
            void Advise();
            void Unadvise();
            void SetOptions(uint fos);
            void GetOptions(out uint fos);
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder(out IShellItem ppsi);
            void GetCurrentSelection(out IShellItem ppsi);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void GetResult(out IShellItem ppsi);
            void AddPlace();
            void SetDefaultExtension();
            void Close();
            void SetClientGuid();
            void ClearClientData();
            void SetFilter();
            void GetResults();
            void GetSelectedItems();
        }

        [ComImport]
        [Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
        [ClassInterface(ClassInterfaceType.None)]
        private class FileOpenDialogRCW {}

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            out IShellItem ppv
        );

        private static string SelectFolder(string title, string initialPath) {
            var dialog = (IFileOpenDialog)new FileOpenDialogRCW();
            uint options = 0x20 | 0x40 | 0x8; // FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_NOCHANGEDIR
            dialog.SetOptions(options);
            if (!string.IsNullOrEmpty(title)) dialog.SetTitle(title);
            dialog.SetOkButtonLabel("Select Folder");
            dialog.SetFileNameLabel("Folder:");
            if (!string.IsNullOrEmpty(initialPath) && Directory.Exists(initialPath)) {
                try {
                    IShellItem folder;
                    Guid iid = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");
                    SHCreateItemFromParsingName(initialPath, IntPtr.Zero, iid, out folder);
                    if (folder != null) dialog.SetFolder(folder);
                } catch {}
            }
            int hr = dialog.Show(IntPtr.Zero);
            if (hr == 0) {
                IShellItem item;
                dialog.GetResult(out item);
                string path;
                item.GetDisplayName(0x80058000, out path); // SIGDN_FILESYSPATH
                return path;
            }
            return null;
        }
    }
}
