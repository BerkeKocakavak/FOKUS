using System.Windows;

namespace FokusKararMotoru;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Any(arg => arg.Equals("--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            int exitCode;
            try
            {
                exitCode = SelfTest.Run();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                exitCode = 1;
            }

            Shutdown(exitCode);
            return;
        }

        base.OnStartup(e);
    }
}
