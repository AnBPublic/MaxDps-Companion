using System.Diagnostics;

namespace MaxDpsCompanion;

internal static class Program
{
    // Environment.ProcessPath always resolves to the real .exe location,
    // so settings.ini sits next to the exe (dist\) and not in a temp dir.
    internal static string AppDir { get; } =
        Path.GetDirectoryName(Environment.ProcessPath) is { Length: > 0 } dir
            ? dir
            : AppContext.BaseDirectory;

    [STAThread]
    private static void Main(string[] args)
    {
        // Must happen before any window exists, otherwise Windows virtualises our
        // window coordinates on a scaled display and every sampled pixel is wrong.
        try { Native.SetProcessDpiAwarenessContext(Native.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2); }
        catch (EntryPointNotFoundException) { /* pre-1703 Windows; the manifest default applies */ }

        ApplicationConfiguration.Initialize();

        var settingsPath = Path.Combine(AppDir, "settings.ini");
        var settings = AppSettings.Load(settingsPath);

        try
        {
            var snapshotArg = args.FirstOrDefault(arg => arg.StartsWith("--ui-snapshot=", StringComparison.OrdinalIgnoreCase));
            if (snapshotArg is not null)
            {
                using var window = new MainForm(settings);
                window.StartPosition = FormStartPosition.Manual;
                window.Location = new Point(-32000, -32000);
                window.ShowInTaskbar = false;
                window.Show();
                Application.DoEvents();
                window.PerformLayout();
                window.Refresh();
                using var bitmap = new Bitmap(window.ClientSize.Width, window.ClientSize.Height);
                window.DrawToBitmap(bitmap, window.ClientRectangle);
                bitmap.Save(snapshotArg[(snapshotArg.IndexOf('=') + 1)..]);
                return;
            }
            if (args.Contains("--ui-smoke-test", StringComparer.OrdinalIgnoreCase))
            {
                using var window = new MainForm(settings);
                window.CreateControl();
                return;
            }
            Application.Run(new MainForm(settings));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "MaxDPS Companion crashed",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            Debug.WriteLine(ex);
        }
    }
}
