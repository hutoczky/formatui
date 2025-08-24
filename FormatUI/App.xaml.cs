using Microsoft.UI.Xaml;
using System;
using System.Linq;

namespace FormatUI
{
    /// <summary>
    /// Application entry point.  Instantiates the main window and passes any
    /// command‑line drive argument for initial selection.
    /// </summary>
    public partial class App : Application
    {
        private Window? _window;

        public App()
        {
            InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            _window = new MainWindow();

            // If the user passed a drive path on the command line (e.g. "E:\"),
            // pass it to the main window so the corresponding volume is
            // pre‑selected.
            try
            {
                var cmdArgs = Environment.GetCommandLineArgs();
                if (cmdArgs.Length > 1)
                {
                    var driveRoot = cmdArgs.Skip(1).FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(driveRoot) && _window is MainWindow mw)
                    {
                        mw.InitializeWithDrive(driveRoot.Trim());
                    }
                }
            }
            catch
            {
                // Ignore command line parsing errors; simply proceed with the UI.
            }

            _window.Activate();
        }
    }
}