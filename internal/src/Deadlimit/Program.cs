using Deadlimit.App;

namespace Deadlimit;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var form = new MainForm();
        BuildFeature.Attach(form);
        Application.Run(form);
    }
}
