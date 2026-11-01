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

        // "0.1+abc1234": the version, then the commit it was built from ("dev" for a build made
        // anywhere but GitHub). build.cmd passes the commit in; it becomes the exe's Product version.
        public static readonly string FullVersion =
            typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0+dev";

        public static string Version => FullVersion.Split('+')[0];

        public static string Build
        {
            get
            {
                int plus = FullVersion.IndexOf('+');
                return plus < 0 ? "dev" : FullVersion.Substring(plus + 1);
            }
        }

        [STAThread]
        static int Main(string[] args)
        {
            CommandLine line = CommandLine.Parse(args);
            if (line.Problem != null)
            {
                MessageBox.Show(line.Problem + "\n\n" + CommandLine.Usage, Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                return 2;
            }
            DataFolder.Choose(line.DataFolder);
            if (line.SelfTestReport != null) return SelfTest.Run(line.SelfTestReport);
            DataFolder.CheckWritable();

            var app = new App();
            app.InitializeComponent();
            Settings settings = Settings.Load();
            Theme.Start(settings.Look);
            int code = app.Run(new MainWindow(settings));
            Theme.Stop();
            return code;
        }
    }
}
