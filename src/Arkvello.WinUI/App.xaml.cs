using Microsoft.UI.Xaml;
namespace Arkvello;
public partial class App : Application
{
    private Window? window;
    public App()
    {
        if (Environment.GetCommandLineArgs().Contains("--diagnostics"))
        {
            DebugSettings.IsXamlResourceReferenceTracingEnabled = true;
            DebugSettings.XamlResourceReferenceFailed += (_, e) =>
            {
                Directory.CreateDirectory(Path.GetFullPath("build"));
                File.AppendAllText(Path.GetFullPath("build/winui-resource-error.txt"), e.Message + Environment.NewLine);
            };
        }
        UnhandledException += (_, e) => WriteStartupFailure(e.Exception);
        try { InitializeComponent(); }
        catch (Exception error) { WriteStartupFailure(error); throw; }
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            window = new MainWindow();
            window.Activate();
        }
        catch (Exception error) { WriteStartupFailure(error); throw; }
    }
    private static void WriteStartupFailure(Exception error)
    {
        // Opt-in local diagnostic, never log operation parameters or secrets.
        if (!Environment.GetCommandLineArgs().Contains("--diagnostics")) return;
        string path = Path.GetFullPath("build/winui-startup-error.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, error.ToString());
    }
}
