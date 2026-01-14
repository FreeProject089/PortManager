using System.Configuration;
using System.Data;
using System.IO;
using System.Text;
using System.Windows;

namespace PortManager;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        DispatcherUnhandledException += App_DispatcherUnhandledException;
    }

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash(e.Exception);
        e.Handled = true; 
        System.Windows.MessageBox.Show($"Une erreur critique est survenue. Voir les logs.\n\n{e.Exception.Message}", "Crash", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        Shutdown();
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogCrash(ex);
            System.Windows.MessageBox.Show($"Une erreur fatale est survenue.\n\n{ex.Message}", "Fatal Crash", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private void LogCrash(Exception ex)
    {
        try
        {
            string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            if (!Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir);
            }

            string filename = $"crash_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            string filePath = Path.Combine(logDir, filename);

            var sb = new StringBuilder();
            sb.AppendLine("=== CRASH REPORT ===");
            sb.AppendLine($"Time: {DateTime.Now}");
            sb.AppendLine($"Version: 1.2 Ultimate");
            sb.AppendLine("--------------------");
            sb.AppendLine($"Message: {ex.Message}");
            sb.AppendLine($"Source: {ex.Source}");
            sb.AppendLine($"Stack Trace:");
            sb.AppendLine(ex.StackTrace);
            
            if (ex.InnerException != null)
            {
                sb.AppendLine("--------------------");
                sb.AppendLine("Inner Exception:");
                sb.AppendLine(ex.InnerException.Message);
                sb.AppendLine(ex.InnerException.StackTrace);
            }

            File.WriteAllText(filePath, sb.ToString());
        }
        catch 
        {
            // Failed to log crash... nothing more we can do
        }
    }
}

