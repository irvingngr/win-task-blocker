using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;

namespace WinTaskBlocker;

public partial class App : System.Windows.Application
{
    private const string MutexName = @"Global\WinTaskBlocker_SingleInstance_Mutex_v2";
    private const string ActivateEventName = @"Global\WinTaskBlocker_ActivateWindow_Event_v2";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _activateEvent;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_RESTORE = 9;

    protected override void OnStartup(StartupEventArgs e)
    {
        _instanceMutex = new Mutex(true, MutexName, out bool createdNew);

        if (!createdNew)
        {
            // Another instance is already running -> signal it to come to the front and exit this duplicate
            try
            {
                using var existingEvent = EventWaitHandle.OpenExisting(ActivateEventName);
                existingEvent.Set();
            }
            catch { }

            Shutdown();
            return;
        }

        // First instance: create activation listener thread so any subsequent launch brings this window to foreground
        try
        {
            _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
            var listenerThread = new Thread(WaitForSecondInstanceActivation)
            {
                IsBackground = true,
                Name = "WinTaskBlocker_SingleInstanceListener"
            };
            listenerThread.Start();
        }
        catch { }

        base.OnStartup(e);

        var mainWin = new MainWindow();
        MainWindow = mainWin;
        mainWin.Show();
    }

    private void WaitForSecondInstanceActivation()
    {
        while (_activateEvent != null)
        {
            try
            {
                _activateEvent.WaitOne();
                Dispatcher.Invoke(() =>
                {
                    if (MainWindow is MainWindow win)
                    {
                        win.BringToFrontFromSecondInstance();
                        var hwnd = new WindowInteropHelper(win).Handle;
                        if (hwnd != IntPtr.Zero)
                        {
                            ShowWindow(hwnd, SW_RESTORE);
                            SetForegroundWindow(hwnd);
                        }
                    }
                });
            }
            catch
            {
                break;
            }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _activateEvent?.Dispose();
            _instanceMutex?.ReleaseMutex();
            _instanceMutex?.Dispose();
        }
        catch { }
        base.OnExit(e);
    }
}
