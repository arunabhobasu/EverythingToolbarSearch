using EverythingQuickSearch.Util;
using EverythingQuickSearch.Properties;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace EverythingQuickSearch
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private const string SingleInstanceMutexName = "EverythingQuickSearch_SingleInstance";
        private Mutex? _singleInstanceMutex;

        protected override async void OnStartup(StartupEventArgs e)
        {
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Critical;
            base.OnStartup(e);

            _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out bool createdNew);
            if (!createdNew)
            {
                // Another instance is already running — bring it to the foreground and exit.
                var existing = Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName)
                    .FirstOrDefault(p => p.Id != Environment.ProcessId);
                if (existing != null)
                {
                    var hwnd = existing.MainWindowHandle;
                    if (hwnd != IntPtr.Zero)
                    {
                        try
                        {
                            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
                            NativeMethods.SetForegroundWindow(hwnd);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Could not bring existing instance to foreground: {ex.Message}");
                        }
                    }
                }
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
                Shutdown();
                return;
            }

            try
            {
                await StartupAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Startup error: {ex}");
                Shutdown();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
            base.OnExit(e);
        }

        private static class NativeMethods
        {
            public const int SW_RESTORE = 9;

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            public static extern bool SetForegroundWindow(IntPtr hWnd);

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        }

        private async Task StartupAsync()
        {
            string installerPath = Path.Combine(AppContext.BaseDirectory, "Installer", "Everything-Setup.exe");

            if (!EverythingInstaller.IsEverythingRunning() && !EverythingInstaller.IsEverythingInstalled())
            {
                bool proceed = await ShowInstallPromptAsync(installerPath);
                if (!proceed)
                {
                    Shutdown();
                    return;
                }
            }

            var mainWindow = new MainWindow();
            mainWindow.Show();
        }

        private static async Task<bool> ShowInstallPromptAsync(string installerPath)
        {
            var stackPanel = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            stackPanel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = Lang.Everything_NotInstalled_Message,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Padding = new Thickness(0, 0, 0, 10),
            });

            stackPanel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "Would you like to install it now? (Administrator privileges are required.)",
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Padding = new Thickness(0, 0, 0, 10),
            });

            var dialog = new Wpf.Ui.Controls.MessageBox
            {
                Title = "Everything Quick Search",
                Content = stackPanel,
                PrimaryButtonText = Lang.Everything_NotInstalled_InstallButton,
                CloseButtonText = Lang.Everything_Error_MissingDll_MessageBox_CloseButtonText,
                MinWidth = 10,
            };

            var result = await dialog.ShowDialogAsync();

            if (result != Wpf.Ui.Controls.MessageBoxResult.Primary)
                return false;

            bool installed = await EverythingInstaller.InstallEverythingAsync(installerPath);
            if (!installed)
            {
                var errorDialog = new Wpf.Ui.Controls.MessageBox
                {
                    Title = "Everything Quick Search",
                    Content = new System.Windows.Controls.TextBlock
                    {
                        Text = "Everything installation did not complete successfully. Please install Everything manually from https://www.voidtools.com and restart the app.",
                        TextWrapping = TextWrapping.Wrap,
                    },
                    CloseButtonText = Lang.Everything_Error_MissingDll_MessageBox_CloseButtonText,
                    MinWidth = 10,
                };
                await errorDialog.ShowDialogAsync();
                return false;
            }

            bool started = await EverythingInstaller.StartEverythingServiceAsync();
            if (!started)
            {
                var warnDialog = new Wpf.Ui.Controls.MessageBox
                {
                    Title = "Everything Quick Search",
                    Content = new System.Windows.Controls.TextBlock
                    {
                        Text = Lang.Everything_StartFailed_Message,
                        TextWrapping = TextWrapping.Wrap,
                    },
                    CloseButtonText = Lang.Everything_Error_MissingDll_MessageBox_CloseButtonText,
                    MinWidth = 10,
                };
                await warnDialog.ShowDialogAsync();
                return false;
            }

            return true;
        }
    }
}
