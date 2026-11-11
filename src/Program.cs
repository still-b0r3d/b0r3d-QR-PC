using System;
using System.Reflection;
using System.Windows;

namespace B0r3dQR
{
    static class Program
    {
        public const string Name = "b0r3d QR";
        public const string Maker = "b0r3d.org";
        public const string MakerUrl = "https://b0r3d.org";
        // Help → Check for a new version opens this in the browser; b0r3d QR never looks by itself
        public const string ReleasesUrl = "https://github.com/still-b0r3d/b0r3d-QR-PC/releases";

        // "0.3 build abc1234", the exe's Product version: the version, then the commit it was built
        // from ("dev" for a build made anywhere but GitHub). build.cmd passes the commit in.
        public static readonly string FullVersion =
            typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0 build dev";

        public static string Version => FullVersion.Split(' ')[0];

        public static string Build
        {
            get
            {
                int at = FullVersion.LastIndexOf(" build ", StringComparison.Ordinal);
                return at < 0 ? "dev" : FullVersion.Substring(at + " build ".Length);
            }
        }

        // as it's shown, the way LoafLock shows its own: "v0.3 · Build: abc1234"
        public static string VersionText => "v" + Version + " · Build: " + Build;

        [STAThread]
        static int Main(string[] args)
        {
            // before WPF draws anything, so the program closes cleanly on PCs without a graphics card
            Scan.PdfPages.LoadEarly();
            CommandLine line = CommandLine.Parse(args);
            if (line.Problem != null)
            {
                MessageBox.Show(line.Problem + "\n\n" + CommandLine.Usage, Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                return 2;
            }
            DataFolder.Choose(line.DataFolder);
            if (line.SelfTestReport != null) return SelfTest.Run(line.SelfTestReport);
            // one b0r3d QR for each data folder: started again, it brings the one running forward
            if (!SingleCopy.Claim(DataFolder.Path))
            {
                SingleCopy.BringForward();
                return 0;
            }
            try
            {
                DataFolder.CheckWritable();
                var app = new App();
                app.InitializeComponent();
                Settings settings = Settings.Load();
                Theme.Start(settings.Look);
                int code = app.Run(new MainWindow(settings));
                Theme.Stop();
                return code;
            }
            finally
            {
                SingleCopy.Release();
            }
        }
    }
}
