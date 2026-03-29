using System.Windows;
using Wpf.Ui.Controls;
using MenuItem = System.Windows.Controls.MenuItem;
using TextBlock = System.Windows.Controls.TextBlock;

namespace EverythingQuickSearch
{
    public partial class SettingsWindow : FluentWindow
    {
        MainWindow _window;
        public SettingsWindow(MainWindow window)
        {
            InitializeComponent();
            _window = window;
            BackgroundDropDownButton.Content = _window.Settings.TransparentBackground ? "Transparent" : "Default";
            LoadBackgroundContextMenu();

            // Initialize new setting controls
            PageSizeBox.Value = _window.Settings.PageSize;
            DefaultSortBox.Value = _window.Settings.DefaultSort;
            EnableRegexByDefaultCheckBox.IsChecked = _window.Settings.EnableRegexByDefault;
            WindowOpacitySlider.Value = _window.Settings.WindowOpacity;
            WindowOpacityLabel.Text = $"{_window.Settings.WindowOpacity:P0}";
        }

        private void LoadBackgroundContextMenu()
        {
            var sortValues = new List<string> {
                "Default",
                "Transparent"
            };

            int id = 0;
            int fontSize = 13;
            int height = 31;
            Thickness padding = new Thickness(0, 0, 8, 0);

            foreach (var item in sortValues)
            {
                MenuItem menuitem = new MenuItem
                {
                    Header = new TextBlock
                    {
                        Text = item,
                        FontSize = fontSize,
                        Padding = padding
                    },
                    Height = height,
                    Tag = id,
                };

                menuitem.Click += (_, _) =>
                {
                    _window.Settings.TransparentBackground = ((int)menuitem.Tag == 1);
                    BackgroundDropDownButton.Content = ((TextBlock)menuitem.Header).Text;
                };
                id++;
                BackgroundContextMenu.Items.Add(menuitem);
            }
        }

        private void PageSizeBox_ValueChanged(object sender, NumberBoxValueChangedEventArgs args)
        {
            if (_window != null && args.NewValue.HasValue && !double.IsNaN(args.NewValue.Value))
            {
                _window.Settings.PageSize = Math.Clamp((int)args.NewValue.Value, 5, 200);
            }
        }

        private void DefaultSortBox_ValueChanged(object sender, NumberBoxValueChangedEventArgs args)
        {
            if (_window != null && args.NewValue.HasValue && !double.IsNaN(args.NewValue.Value))
            {
                _window.Settings.DefaultSort = Math.Clamp((int)args.NewValue.Value, 1, 26);
            }
        }

        private void EnableRegexByDefaultCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_window != null)
            {
                _window.Settings.EnableRegexByDefault = EnableRegexByDefaultCheckBox.IsChecked == true;
            }
        }

        private void WindowOpacitySlider_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            if (_window != null)
            {
                double opacity = Math.Clamp(e.NewValue, 0.5, 1.0);
                _window.Settings.WindowOpacity = opacity;
                _window.Opacity = opacity;
                if (WindowOpacityLabel != null)
                    WindowOpacityLabel.Text = $"{opacity:P0}";
            }
        }
    }
}
