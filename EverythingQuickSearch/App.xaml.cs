using EverythingQuickSearch.Util;
using System.Diagnostics;
using System.IO;
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
        protected override async void OnStartup(StartupEventArgs e)
        {
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Critical;
            base.OnStartup(e);

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
                Text = "Everything Search is required by this app but is not installed on your system.",
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
                PrimaryButtonText = "Install Everything",
                CloseButtonText = "Exit",
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
                    CloseButtonText = "Exit",
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
                        Text = "Everything was installed but could not be started automatically. Please start Everything manually and then relaunch this app.",
                        TextWrapping = TextWrapping.Wrap,
                    },
                    CloseButtonText = "Exit",
                    MinWidth = 10,
                };
                await warnDialog.ShowDialogAsync();
                return false;
            }

            return true;
        }
    }
}
