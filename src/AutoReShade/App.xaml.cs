using System.Windows;
using System.Windows.Threading;
using AutoReShade.Core;
using AutoReShade.Services;

namespace AutoReShade;

public partial class App : Application
{
    private const string InstanceMutexName = @"Local\AutoReShade.SingleInstance";
    private const string ShowEventName = @"Local\AutoReShade.ShowWindow";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _showEvent;
    private AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var isFirst);
        if (!isFirst)
        {
            // Already running: ask the running copy to show its window, then quit.
            try
            {
                using var show = EventWaitHandle.OpenExisting(ShowEventName);
                show.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
            }
            Shutdown();
            return;
        }

        var paths = AppPaths.CreateDefault();
        paths.EnsureCreated();
        Log.Initialize(paths.LogsDir);
        Log.Info($"AutoReShade {typeof(App).Assembly.GetName().Version} starting");

        DispatcherUnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Error("Unhandled exception", args.ExceptionObject as Exception);

        _controller = new AppController(paths);
        ListenForSecondInstance();

        var minimized = e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase)) || _controller.Settings.StartMinimized;
        _controller.Start(showWindow: !minimized);
    }

    private void ListenForSecondInstance()
    {
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var thread = new Thread(() =>
        {
            while (_showEvent.WaitOne())
                Dispatcher.BeginInvoke(() => _controller?.ShowSettings());
        })
        { IsBackground = true, Name = "AutoReShade single instance" };
        thread.Start();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unexpected error", e.Exception);
        MessageBox.Show(
            $"Something went wrong:\n\n{e.Exception.Message}\n\nAutoReShade will keep running. Details were written to the log file:\n{Log.FilePath}",
            "AutoReShade", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        Log.Info("AutoReShade closed");
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
