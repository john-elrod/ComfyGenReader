using System.Windows;
using ComfyGenReader.Controls;
using ComfyGenReader.Core;
using ComfyGenReader.Core.Metadata;

namespace ComfyGenReader;

public partial class App : Application
{
    private ReaderSession? session;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            ReportError(args.Exception);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            args.SetObserved();
            ReportError(args.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            ReportError(args.ExceptionObject as Exception
                        ?? new InvalidOperationException($"Non-exception error: {args.ExceptionObject}"));

        VideoPlayer.StartLibVlcWarmUp();

        try
        {
            var settings = new AppSettings(AppSettings.DefaultPath);
            Theme.Apply(this, settings.DarkMode);
            session = new ReaderSession(new MetadataLoader(), new RecentFiles(RecentFiles.DefaultPath));
            new MainWindow(session, settings).Show();
        }
        catch (Exception ex)
        {
            // Without a window the app has nowhere to show anything; report and exit cleanly.
            ShowMessageBox(ex);
            Shutdown(1);
        }
    }

    /// <summary>Routes an error from any thread to the Raw panel (or a message box before the window exists).</summary>
    private void ReportError(Exception ex)
    {
        if (Dispatcher.CheckAccess()) Show();
        else Dispatcher.InvokeAsync(Show);

        void Show()
        {
            if (session is not null && MainWindow is { IsLoaded: true }) session.ShowError(ex);
            else ShowMessageBox(ex);
        }
    }

    private static void ShowMessageBox(Exception ex) =>
        MessageBox.Show(ErrorFormatter.Format(ex), "ComfyGenReader", MessageBoxButton.OK, MessageBoxImage.Error);
}
