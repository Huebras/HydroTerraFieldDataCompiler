using System.Text;

namespace HydroTerraFieldDataCompiler;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        try
        {
            ApplicationConfiguration.Initialize();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, args) => ShowStartupError(args.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                ShowStartupError(args.ExceptionObject as Exception ?? new Exception("Unknown application error."));

            Application.Run(new MainWizardForm());
        }
        catch (Exception ex)
        {
            ShowStartupError(ex);
        }
    }

    private static void ShowStartupError(Exception exception)
    {
        try
        {
            string logPath = Path.Combine(AppContext.BaseDirectory, "HydroTerraFieldDataCompiler_error.log");
            var text = new StringBuilder()
                .AppendLine(DateTime.Now.ToString("O"))
                .AppendLine(exception.ToString())
                .ToString();
            File.AppendAllText(logPath, text);

            MessageBox.Show(
                "HydroTerra Field Data Compiler could not start.\n\n" +
                exception.Message + "\n\nA diagnostic log was written to:\n" + logPath,
                "HydroTerra Startup Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch
        {
            // Avoid throwing another exception while reporting the original startup error.
        }
    }
}
