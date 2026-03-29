using System.Diagnostics;
using System.Windows;

namespace EverythingQuickSearch
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Critical;
            base.OnStartup(e);
        }
    }
}
