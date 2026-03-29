using EverythingQuickSearch.Core;
using EverythingQuickSearch.Properties;
using EverythingQuickSearch.Util;
using Gma.System.MouseKeyHook;
using IWshRuntimeLibrary;
using Microsoft.WindowsAPICodePack.Shell;
using StringMath;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Wpf.Ui;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using File = System.IO.File;
using MenuItem = Wpf.Ui.Controls.MenuItem;
using Point = System.Windows.Point;
using Registry = Microsoft.Win32.Registry;
using Task = System.Threading.Tasks.Task;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace EverythingQuickSearch
{
    public partial class MainWindow : FluentWindow
    {
        #region import
        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint WINEVENT_OUTOFCONTEXT = 0;

        private const int DWMWA_TRANSITIONS_FORCEDISABLED = 3;

        [SecurityCritical]
        [DllImport("dwmapi.dll", SetLastError = false, ExactSpelling = true)]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, uint dwAttribute, ref int pvAttribute, int cbAttribute);


        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType,
            IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
            WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [Flags]
        public enum KeyboardEventFlags : uint
        {
            KeyDown = 0x0000,
            KeyUp = 0x0002
        }

        public const byte VK_ESCAPE = 0x1B;

        [DllImport("user32.dll")]
        public static extern void keybd_event(byte bVk, byte bScan, KeyboardEventFlags dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        const int WM_CLOSE = 0x0010;

        [DllImport("user32.dll")]
        static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool SHObjectProperties(IntPtr hwnd, uint shopObjectType, string pszObjectName, string? pszPropertyPage);
        private const uint SHOP_FILEPATH = 0x2;

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();



        [DllImport("kernel32.dll")]
        static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);


        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(int uiAction, int uiParam, out bool pvParam, int fWinIni);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(int uiAction, int uiParam, bool pvParam, int fWinIni);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(int uiAction, int uiParam, ref ANIMATIONINFO pvParam, int fWinIni);

        private const int SPI_GETCLIENTAREAANIMATION = 0x1042;
        private const int SPI_SETCLIENTAREAANIMATION = 0x1043;
        private const int SPI_GETANIMATION = 0x0048;
        private const int SPI_SETANIMATION = 0x0049;

        private const int SPIF_UPDATEINIFILE = 0x01;
        private const int SPIF_SENDCHANGE = 0x02;

        [StructLayout(LayoutKind.Sequential)]
        public struct ANIMATIONINFO
        {
            public int cbSize;
            public int iMinAnimate;
        }

        #endregion

        private static readonly HashSet<System.Windows.Forms.Keys> NonCharacterKeys = new()
        {
            System.Windows.Forms.Keys.Escape,
            System.Windows.Forms.Keys.LWin,
            System.Windows.Forms.Keys.RWin,
            System.Windows.Forms.Keys.LMenu,
            System.Windows.Forms.Keys.RMenu,
            System.Windows.Forms.Keys.LControlKey,
            System.Windows.Forms.Keys.RControlKey,
            System.Windows.Forms.Keys.LShiftKey,
            System.Windows.Forms.Keys.RShiftKey,
            System.Windows.Forms.Keys.Tab,
            System.Windows.Forms.Keys.Back,
            System.Windows.Forms.Keys.CapsLock,
        };
        private RegistryHelper reg = new RegistryHelper("EverythingQuickSearch");
        private uint _taskbarRestartMessage;

        public ObservableCollection<FileItem> FileItems { get; set; }
        public ObservableCollection<FileItem> AppItems { get; set; }
        private EverythingService? _everything;

        private readonly Dictionary<string, FileItem> _appItemMap = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FileItem> _fileItemMap = new(StringComparer.OrdinalIgnoreCase);

        private ThumbnailGenerator thumbnailGenerator;

        private FileItem? _selectedItem;
        private bool _selectedItemIsFile;
        private CancellationTokenSource? _thumbnailCts;

        private RECT _searchHostRect;
        IntPtr _searchHwnd = IntPtr.Zero;

        private bool _lookForKeyDown = false;
        private bool _isShowing;
        private bool originalAnimationState;
        private bool _originalMinAnimationState;

        private IntPtr _hookForeground = IntPtr.Zero;
        private WinEventDelegate _winEventDelegate;
        private GCHandle _winEventDelegateHandle;
        private IKeyboardMouseEvents m_GlobalHook;
        static BitmapSource? _defaultFolderIcon;
        private bool _darkModeApplication = true;
        private bool _darkModeSearchBar = true;
        private bool didMath = false;

        bool _isStartCentered = false;

        CancellationTokenSource? _searchCts;

        private string _currentQuery = string.Empty;
        private string _lastSearchText = string.Empty;
        private int PageSize => Settings.PageSize;
        private CancellationTokenSource? _debounceCts;
        private const int DebounceDelayMs = 150;
        private volatile string forwardText = "";

        private int _currentFileOffset = 0;
        private int _currentAppOffset = 0;
        private bool _hasMoreAppResults = true;
        private bool _hasMoreFileResults = true;
        private bool enableRegex = false;

        private int _setSort = 1;
        private bool _setSortAscending = true;
        private int _currentSortId = 1;

        private bool _isAppLoading = false;
        private bool _isFileLoading = false;
        private string shortcutFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Everything Quick Search");

        private readonly SemaphoreSlim _thumbnailSemaphore = new SemaphoreSlim(Math.Max(Environment.ProcessorCount - 1, 1));

        private const int MaxRecentItems = 10;
        private const string RecentItemsRegistryKey = "RecentItems";
        private List<string> _recentItems = new List<string>();

        // Tracks whether UWP app shortcuts have been created at least once.
        // LoadUwpApps is expensive; only refresh every 5 minutes at most.
        private DateTime _uwpAppsLastLoaded = DateTime.MinValue;
        private static readonly TimeSpan UwpAppsCacheWindow = TimeSpan.FromMinutes(5);

        private string _categoryFilter = string.Empty;
        private Button? _selectedCategoryButton;

        SolidColorBrush SelectedItemBarBrush = new SolidColorBrush(Colors.Red);
        SolidColorBrush TemplateSecondaryTextBrush = new SolidColorBrush(Colors.Red);
        public Settings Settings { get; set; }
        public MainWindow()
        {
            SystemThemeWatcher.Watch(this); // in 4.2.0 not working https://github.com/lepoco/wpfui/issues/1656
            this.Settings = new Settings();
            InitializeComponent();
            this.Language = XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);

            // Apply persisted settings
            this.Opacity = Settings.WindowOpacity;
            enableRegex = Settings.EnableRegexByDefault;
            _currentSortId = Settings.DefaultSort;
            _setSort = (_currentSortId * 2) - (_setSortAscending ? 1 : 0);

            AutorunToggle.IsChecked = reg.ReadKeyValueRootBool("startOnLogin");

            versionHeader.Header += Process.GetCurrentProcess().MainModule?.FileVersionInfo.FileVersion is string v ? $" {v}" : string.Empty;
            LoadUwpApps();
            _uwpAppsLastLoaded = DateTime.UtcNow;
            Application.Current.Resources["SelectedItemBarBrush"] = SelectedItemBarBrush;
            Application.Current.Resources["TemplateSecondaryTextBrush"] = TemplateSecondaryTextBrush;

            _darkModeApplication = (int?)Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 1) == 0;
            _darkModeSearchBar = (int?)Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) == 0;
            UpdateWPFUITheme();
            _defaultFolderIcon = GetDefaultFolderIcon(16);
            FileItems = new ObservableCollection<FileItem>();
            AppItems = new ObservableCollection<FileItem>();
            thumbnailGenerator = new ThumbnailGenerator();

            this.DataContext = this;

            this.IsVisibleChanged += (s, e) =>
            {
                if (IsVisible)
                {
                    Debug.WriteLine("can hide");
                }
            };

            m_GlobalHook = Hook.GlobalEvents();
            m_GlobalHook.KeyPress += GlobalHookKeyPress;
            m_GlobalHook.KeyDown += M_GlobalHook_KeyDown;
            _winEventDelegate = new WinEventDelegate(WinEventProc);
            // Pin the delegate to prevent it from being garbage-collected while native code holds a pointer to it.
            _winEventDelegateHandle = GCHandle.Alloc(_winEventDelegate);
            _hookForeground = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero,
            _winEventDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
            Populate_Sortby_DropDownButton_ContextMenu();
            LoadRecentItems();

            AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
            {
                try
                {
                    SystemParametersInfo(SPI_SETCLIENTAREAANIMATION, 0, originalAnimationState, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
                    SetMinimizeAnimation(_originalMinAnimationState);
                }
                catch { }
            };
        }

        protected override async void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            this.Visibility = Visibility.Hidden;
            this.Top = 10000;

            int disable = 1;
            DwmSetWindowAttribute(new WindowInteropHelper(this).Handle,
                DWMWA_TRANSITIONS_FORCEDISABLED,
                ref disable,
                sizeof(int));

            string installerPath = Path.Combine(AppContext.BaseDirectory, "Installer", "Everything-Setup.exe");

            while (true)
            {
                try
                {
                    _everything?.Dispose();
                    _everything = new EverythingService(this);
                    var (major, minor, revision) = _everything.GetVersion();
                    if (major < 1 || (major == 1 && minor < 4) || (major == 1 && minor == 4 && revision < 1))
                    {
                        var versionWarning = new Wpf.Ui.Controls.MessageBox
                        {
                            Title = "Everything Quick Search",
                            Content = new TextBlock
                            {
                                Text = $"Everything {major}.{minor}.{revision} is connected. Everything 1.4.1 or later is recommended for full compatibility.",
                                TextWrapping = TextWrapping.Wrap,
                            },
                            CloseButtonText = "OK",
                            MinWidth = 10
                        };
                        _ = versionWarning.ShowDialogAsync();
                    }
                    break;
                }
                catch (Exception)
                {
                    bool isInstalled = EverythingInstaller.IsEverythingInstalled();
                    bool isRunning = EverythingInstaller.IsEverythingRunning();

                    var stackPanel = new StackPanel
                    {
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    string message;
                    string primaryButtonText;

                    if (!isInstalled)
                    {
                        message = Lang.Everything_NotInstalled_Message;
                        primaryButtonText = Lang.Everything_NotInstalled_InstallButton;
                    }
                    else if (!isRunning)
                    {
                        message = Lang.Everything_NotRunning_Message;
                        primaryButtonText = Lang.Everything_NotRunning_StartButton;
                    }
                    else
                    {
                        message = Lang.Everything_Error_MissingDll_MessageBox_TextBlock;
                        primaryButtonText = Lang.Everything_Error_MissingDll_MessageBox_PrimaryButtonText;
                    }

                    stackPanel.Children.Add(new TextBlock
                    {
                        Text = message,
                        TextAlignment = TextAlignment.Center,
                        TextWrapping = TextWrapping.Wrap,
                        Padding = new Thickness(0, 0, 0, 10)
                    });

                    var dialog = new Wpf.Ui.Controls.MessageBox
                    {
                        Title = "Everything Quick Search",
                        Content = stackPanel,
                        PrimaryButtonText = primaryButtonText,
                        CloseButtonText = Lang.Everything_Error_MissingDll_MessageBox_CloseButtonText,
                        MinWidth = 10
                    };

                    var result = await dialog.ShowDialogAsync();

                    if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
                    {
                        bool ready = await EverythingInstaller.EnsureEverythingReadyAsync(installerPath);
                        if (!ready)
                        {
                            Application.Current.Shutdown();
                            return;
                        }
                    }
                    else
                    {
                        Application.Current.Shutdown();
                        return;
                    }
                }
            }
            LoadSearchIcon();
        }

        private void UpdateWPFUITheme()
        {
            _darkModeApplication = (int?)Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                                                            "SystemUsesLightTheme", 1) == 0;
            _darkModeSearchBar = (int?)Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                                                          "AppsUseLightTheme", 1) == 0;
            bool colorizeBackground = (int?)Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                                                          "ColorPrevalence", 0) == 1;

            ApplicationTheme theme = _darkModeApplication ? ApplicationTheme.Dark : Settings.TransparentBackground ? ApplicationTheme.Dark : ApplicationTheme.Light;
            theme = colorizeBackground ? ApplicationTheme.Dark : theme;
            ApplicationThemeManager.Apply(theme);
            WindowBackgroundManager.UpdateBackground(UiApplication.Current.MainWindow, theme, WindowBackdropType.Acrylic);
            this.SetResourceReference(Button.BackgroundProperty, "ControlOnImageFillColorDefaultBrush");
            Brush searchBarForegroundColor;
            if (Settings.TransparentBackground)
            {
                Application.Current.Resources["TemplateSecondaryTextBrush"] = (SolidColorBrush)Application.Current.Resources["SystemFillColorSolidNeutralBrush"];
                this.SetResourceReference(BackgroundProperty, Brushes.Transparent);
                SearchBorder.SetResourceReference(BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
                searchBarForegroundColor = Brushes.White;
            }
            else
            {
                if (_darkModeSearchBar)
                {
                    SearchBorder.SetResourceReference(BackgroundProperty, "ApplicationBackgroundBrush");
                    searchBarForegroundColor = Brushes.White;
                }
                else
                {
                    SearchBorder.Background = Brushes.White;
                    searchBarForegroundColor = Brushes.Black;
                }
                if (_darkModeApplication)
                {
                    Application.Current.Resources["TemplateSecondaryTextBrush"] = (SolidColorBrush)Application.Current.Resources["TextFillColorDisabledBrush"];
                }
                else
                {
                    Application.Current.Resources["TemplateSecondaryTextBrush"] = (SolidColorBrush)Application.Current.Resources["SystemFillColorSolidNeutralBrush"];
                }
            }
            SearchBarTextBox.Foreground = searchBarForegroundColor;
            SearchBarTextBox.CaretBrush = searchBarForegroundColor;
            if (colorizeBackground)
            {
                ApplicationThemeManager.Apply(ApplicationTheme.Light);

                Brush brush = (Brush)this.FindResource("AccentTextFillColorPrimaryBrush");

                ApplicationThemeManager.Apply(theme);
                WindowBackgroundManager.UpdateBackground(UiApplication.Current.MainWindow, theme, WindowBackdropType.Acrylic);
                var hslColor = ColorExtensions.ToHsl(((SolidColorBrush)brush).Color);

                hslColor.Hue = Math.Clamp(hslColor.Hue + 5, 0, 355);
                hslColor.Lightness = Math.Clamp(hslColor.Lightness + 3, 0, 100);
                hslColor.Saturation = Math.Clamp(hslColor.Saturation, 0, 100);

                var c = ColorExtensions.FromHslToRgb(hslColor.Hue, hslColor.Saturation, hslColor.Lightness);
                this.Background = new SolidColorBrush(Color.FromRgb((byte)c.R, (byte)c.G, (byte)c.B));
                this.Background.Opacity = 0.5;
            }
        }

        private void M_GlobalHook_KeyDown(object? sender, System.Windows.Forms.KeyEventArgs e)
        {
            if (NonCharacterKeys.Contains(e.KeyCode) || e.KeyData.ToString() == "Tab, Alt")
            {
                return;
            }
            if (_lookForKeyDown)
            {
                PostMessage(_searchHwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                e.SuppressKeyPress = true;
                GlobalHookKeyPress(sender, null);
            }
        }

        private void GlobalHookKeyPress(object? sender, System.Windows.Forms.KeyPressEventArgs? e)
        {
            forwardText = string.Empty;
            if (!_lookForKeyDown)
            {
                return;
            }
            if (_lookForKeyDown
                && e != null
                && e.KeyChar.ToString().Length > 0
                && e.KeyChar != '\t'
                && e.KeyChar.ToString() != "\u001b") // ESC char
            {

                _ = Task.Run(async () =>
                {
                    await Dispatcher.InvokeAsync(async () =>
                    {
                        // Category bar uses a ScrollViewer so buttons remain accessible at any window width.
                        if (!IsVisible)
                        {
                            double scaling = GetDpiForWindow(_searchHwnd) / 96.0;
                            var source = PresentationSource.FromVisual(this);
                            if (source == null) return;
                            var transform = source.CompositionTarget.TransformFromDevice;

                            Point dip = transform.Transform(new Point(_searchHostRect.left, _searchHostRect.top));
                            this.Left = dip.X;
                            this.Top = dip.Y + 12;
                            this.Width = (_searchHostRect.right - _searchHostRect.left) / scaling;
                            this.Height = ((_searchHostRect.bottom - _searchHostRect.top) / scaling) - 24;
                            if (_isStartCentered)
                            {
                                this.Left += 12;
                            }
                            Debug.WriteLine("show");
                            this.Show();
                        }

                        if (e.KeyChar.ToString() == "\u0016") // paste (ctrl+v)
                        {
                            forwardText = Clipboard.GetText();
                        }
                        else
                        {
                            forwardText = e.KeyChar.ToString();
                        }

                        await Dispatcher.BeginInvoke(new Action(() => { }), DispatcherPriority.Render);
                        await Dispatcher.BeginInvoke(new Action(() =>
                        {
                            var hwnd = new WindowInteropHelper(this).Handle;

                            uint appThread = GetCurrentThreadId();
                            uint foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);

                            if (foregroundThread != appThread)
                            {
                                AttachThreadInput(appThread, foregroundThread, true);
                                BringWindowToTop(hwnd);
                                SetForegroundWindow(hwnd);
                                AttachThreadInput(appThread, foregroundThread, false);
                            }
                            else
                            {
                                BringWindowToTop(hwnd);
                                SetForegroundWindow(hwnd);
                            }

                            this.Activate();


                            SearchBarTextBox.Text += forwardText;
                            SearchBarTextBox.CaretIndex = SearchBarTextBox.Text.Length;
                            SearchBarTextBox.Focus();

                            _isShowing = true;
                            // set animations to before state
                            Task.Run(() =>
                            {
                                SystemParametersInfo(SPI_SETCLIENTAREAANIMATION, 0, originalAnimationState, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
                                SetMinimizeAnimation(_originalMinAnimationState);
                            });

                        }), DispatcherPriority.ApplicationIdle);


                    }).Task.Unwrap();
                });
                return;
            }
        }

        private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (hwnd == IntPtr.Zero) return;
            GetWindowThreadProcessId(hwnd, out uint pid);
            string processName;
            try
            {
                using var proc = Process.GetProcessById((int)pid);
                processName = proc.ProcessName;
            }
            catch (ArgumentException) { return; }
            if (processName == "SearchHost")
            {
                // set animation off
                Task.Run(() =>
                {
                    SystemParametersInfo(SPI_SETCLIENTAREAANIMATION, 0, false, SPIF_SENDCHANGE);
                    SetMinimizeAnimation(false);
                });

                GetWindowRect(hwnd, out _searchHostRect);
                _ = Task.Run(async () =>
                {

                    await Task.Delay(550); // animation speed, Windows doesn't let get the rect until anima
                    GetWindowRect(hwnd, out _searchHostRect);
                });

                _searchHwnd = hwnd;
                _lookForKeyDown = true;
                // Hard timeout: reset _lookForKeyDown after 500ms to prevent it staying set
                // indefinitely if no keydown event arrives (e.g. search host closed without typing).
                _ = Task.Delay(500).ContinueWith(_ => _lookForKeyDown = false);
                UpdateWPFUITheme();
                ChangeSelectedButton(AllFilterButton);
                changeRegexButtonColor();
                Debug.WriteLine("look for keys..");
                _isStartCentered = (int?)Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                    "TaskbarAl", 1) == 0;
            }
            else if (_lookForKeyDown)
            {
                _ = Task.Run(() =>
                {
                    Thread.Sleep(150); // prevent not forwarding keys when loosing focus on launch

                    //set animations back to oiginal state
                    Task.Run(() =>
                    {
                        SystemParametersInfo(SPI_SETCLIENTAREAANIMATION, 0, originalAnimationState, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
                        SetMinimizeAnimation(_originalMinAnimationState);
                    });
                    _lookForKeyDown = false;
                });
            }
        }

        protected override void OnDeactivated(EventArgs e)
        {
            if (!_isShowing)
            {
                Debug.WriteLine("bad OnDeactivated");
                return;
            }
            _searchCts?.Cancel();
            base.OnDeactivated(e);
            this.Hide();
            _ = Task.Run(() =>
            {
                SystemParametersInfo(SPI_SETCLIENTAREAANIMATION, 0, originalAnimationState, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
                SetMinimizeAnimation(_originalMinAnimationState);
            });
            ChangeSelectedButton(AllFilterButton);
            SearchBarTextBox.Clear();
            if (_selectedItem != null)
            {
                _selectedItem.IsSelected = false;
            }
            SelectedItemPreviewImage.Source = null;
            Debug.WriteLine("OnDeactivated hiding");

            if (enableRegex && !Settings.EnableRegexByDefault)
            {
                enableRegex = false;
                changeRegexButtonColor();
            }
            _isShowing = false;

            // Refresh UWP app shortcuts only if the cache has expired.
            if (DateTime.UtcNow - _uwpAppsLastLoaded > UwpAppsCacheWindow)
            {
                LoadUwpApps();
                _uwpAppsLastLoaded = DateTime.UtcNow;
            }
        }
        public static bool GetMinimizeAnimation()
        {
            var info = new ANIMATIONINFO();
            info.cbSize = Marshal.SizeOf<ANIMATIONINFO>();
            SystemParametersInfo(SPI_GETANIMATION, info.cbSize, ref info, 0);

            return info.iMinAnimate != 0;
        }
        public static void SetMinimizeAnimation(bool enabled)
        {
            var info = new ANIMATIONINFO();
            info.cbSize = Marshal.SizeOf<ANIMATIONINFO>();
            info.iMinAnimate = enabled ? 1 : 0;
            int flags = enabled ? (SPIF_UPDATEINIFILE | SPIF_SENDCHANGE) : SPIF_SENDCHANGE;
            SystemParametersInfo(SPI_SETANIMATION, info.cbSize, ref info, flags);
        }

        private static BitmapSource? GetDefaultFolderIcon(int size)
        {
            if (_defaultFolderIcon != null)
            {
                return _defaultFolderIcon;
            }
            try
            {
                var shellObj = ShellObject.FromParsingName(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

                var thumbnail = shellObj.Thumbnail;
                thumbnail.CurrentSize = new System.Windows.Size(size, size);

                _defaultFolderIcon = thumbnail.BitmapSource;
                _defaultFolderIcon.Freeze();

                shellObj.Dispose();
            }
            catch
            {
                _defaultFolderIcon = null;
            }

            return _defaultFolderIcon;
        }
        private async void SearchBarTextBox_TextChanged(object sender, TextChangedEventArgs? e)
        {
            if (!IsVisible)
            {
                return;
            }
            string searchText = ((TextBox)sender).Text;
            forwardText = string.Empty;

            // Cancel any pending debounce.
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = new CancellationTokenSource();
            var debounceCancellation = _debounceCts.Token;

            try
            {
                await Task.Delay(DebounceDelayMs, debounceCancellation);
            }
            catch (OperationCanceledException)
            {
                // A newer keystroke cancelled this debounce window; also cancel any in-progress
                // search so it does not deliver stale results.
                _searchCts?.Cancel();
                _searchCts?.Dispose();
                _searchCts = null;
                return;
            }
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = new CancellationTokenSource();
            CancellationToken token = _searchCts.Token;

            _currentFileOffset = 0;
            _currentAppOffset = 0;
            _hasMoreAppResults = true;
            _currentQuery = searchText;
            // Clear stale app results so the new query starts fresh (Bug 10).
            AppItems.Clear();
            _appItemMap.Clear();

            App_ScrollViewer.ScrollToTop();
            scrollViewer.ScrollToTop();

            try
            {
                _isAppLoading = false;
                _isFileLoading = false;

                // When query is empty, show recent items instead of running a search
                if (string.IsNullOrEmpty(searchText) && _recentItems.Count > 0)
                {
                    await ShowRecentItemsAsync(token);
                    if (FileItems.Count > 0)
                    {
                        NoSearchResultsGrid.Visibility = Visibility.Collapsed;
                        FilePreviewGrid.Visibility = Visibility.Visible;
                        FileDetails_DynamicScrollViewer.Visibility = Visibility.Visible;
                        didMath = false;
                        await SetPreviewItem(FileItems[0], true);
                    }
                    return;
                }

                await LoadNextAppPageAsync(searchText, token);
                if (AppItems.Count > 0)
                {
                    NoSearchResultsGrid.Visibility = Visibility.Collapsed;
                    FilePreviewGrid.Visibility = Visibility.Collapsed;
                    FileDetails_DynamicScrollViewer.Visibility = Visibility.Visible;

                    didMath = false;
                    await SetPreviewItem(AppItems[0], false);
                }
                await LoadNextFilePageAsync(_categoryFilter + searchText, token, false);

                NoSearchResultsIcon.Foreground = new SolidColorBrush(_darkModeApplication ? Colors.White : Colors.Black);

                if (FileItems.Count > 0 && AppItems.Count == 0)
                {
                    NoSearchResultsGrid.Visibility = Visibility.Collapsed;
                    FilePreviewGrid.Visibility = Visibility.Visible;
                    FileDetails_DynamicScrollViewer.Visibility = Visibility.Visible;

                    didMath = false;
                    await SetPreviewItem(FileItems[0], true);
                }
                else if (FileItems.Count == 0 && AppItems.Count == 0)
                {
                    string savedText = searchText;
                    FileDetails_DynamicScrollViewer.Visibility = Visibility.Collapsed;
                    try
                    {
                        try
                        {
                            NoSearchResultsTextBlock.Text = "= " + searchText.Eval().ToString();
                        }
                        catch
                        {
                            searchText = searchText.Substring(0, searchText.Length - 1);
                            NoSearchResultsTextBlock.Text = "= " + searchText.Eval().ToString();
                        }
                        NoSearchResultsTextBlock.FontSize = 18;
                        NoSearchResultsIcon.Symbol = SymbolRegular.Calculator20;
                        NoSearchResultsIcon.Foreground = new SolidColorBrush(_darkModeApplication ? Colors.White : Colors.Black);
                        didMath = true;
                    }
                    catch
                    {
                        if (!didMath)
                        {
                            NoSearchResultsIcon.Symbol = SymbolRegular.Search24;
                            NoSearchResultsTextBlock.FontSize = 16;
                            NoSearchResultsTextBlock.Text = $"{Lang.SearchWindow_Search_NoResults} \"{savedText}\"";
                            NoSearchResultsIcon.Foreground = new SolidColorBrush(_darkModeApplication ? Colors.White : Colors.Black);
                        }
                        else
                        {
                            NoSearchResultsIcon.Foreground = new SolidColorBrush(Colors.IndianRed);
                        }
                    }
                    NoSearchResultsGrid.Visibility = Visibility.Visible;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"Search error: {ex}");
            }
        }

        private async void LoadSearchIcon() // get windows searh icon
        {
            var tempList = await _everything!.SearchAsync("SearchIconOnDark.scale-200.png", 1, 0, 1, false);
            foreach (var item in tempList)
            {
                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    SearchIconImage.Source = await thumbnailGenerator.GetThumbnailAsync(item.FullPath, 26);
                });
            }

        }

        private async Task LoadNextAppPageAsync(string searchText, CancellationToken token)
        {
            if (_isAppLoading || _everything == null)
            {
                return;
            }
            _isAppLoading = true;

            try
            {
                List<FileItem> tempList;
                try
                {
                    string searchText2 = $"ext:lnk;url;exe {searchText} file: " +
                     "\"" + Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Start Menu", "Programs") + "\" | " +
                     @"""C:\ProgramData\Microsoft\Windows\Start Menu\Programs\"" | "
                + "\"" + shortcutFolder + "\"";

                    tempList = await _everything.SearchAsync(searchText2, 1, _currentAppOffset, 5, false, token);
                    foreach (var item in tempList)
                    {
                        item.Name = Path.GetFileNameWithoutExtension(item.Name);
                    }

                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (token.IsCancellationRequested)
                {
                    return;
                }

                _hasMoreAppResults = tempList.Count >= 5;
                _currentAppOffset += tempList.Count;

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var newPaths = new HashSet<string>(tempList.Select(f => f.FullPath), StringComparer.OrdinalIgnoreCase);

                    for (int i = AppItems.Count - 1; i >= 0; i--)
                    {
                        var path = AppItems[i].FullPath;
                        if (!newPaths.Contains(path))
                        {
                            AppItems.RemoveAt(i);
                            _appItemMap.Remove(path);
                        }
                    }
                    foreach (var item in tempList)
                    {
                        if (!_appItemMap.ContainsKey(item.FullPath))
                        {
                            AppItems.Add(item);
                            _appItemMap[item.FullPath] = item;
                        }
                    }

                    var sorted = AppItems
                        .OrderBy(a => a.Name.Length)
                        .Take(3)
                        .ToList();

                    for (int i = 0; i < sorted.Count; i++)
                    {
                        var currentIndex = AppItems.IndexOf(sorted[i]);
                        if (currentIndex != i)
                            AppItems.Move(currentIndex, i);
                    }
                    App_ScrollViewer.Height = Math.Min(AppItems.Count, 3) * 40;
                });

                try
                {
                    await Parallel.ForEachAsync(tempList, new ParallelOptions
                    {
                        MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1),
                        CancellationToken = token
                    },
                    async (item, ct) =>
                    {
                        if (!_appItemMap.TryGetValue(item.FullPath, out FileItem? target))
                        {
                            return;
                        }
                        await _thumbnailSemaphore.WaitAsync(ct);
                        try
                        {
                            var thumb = Directory.Exists(item.FullPath)
                                ? _defaultFolderIcon
                                : await thumbnailGenerator.GetThumbnailAsync(item.FullPath, 32);

                            if (thumb != null)
                                await Application.Current.Dispatcher.InvokeAsync(() => target.Thumbnail = thumb);
                        }
                        finally
                        {
                            _thumbnailSemaphore.Release();
                        }
                    });
                }
                catch (OperationCanceledException) { }
            }
            finally
            {
                _isAppLoading = false;
            }
        }


        private async Task LoadNextFilePageAsync(string searchText, CancellationToken token, bool categoryChanged)
        {
            if (_isFileLoading || _everything == null)
            {
                return;
            }
            _isFileLoading = true;

            try
            {
                bool queryChanged = searchText != _lastSearchText;

                if (queryChanged || categoryChanged)
                {
                    _currentFileOffset = 0;
                    _lastSearchText = searchText;
                }

                List<FileItem> tempList;
                try
                {
                    tempList = await _everything!.SearchAsync(searchText, _setSort, _currentFileOffset, PageSize, enableRegex, token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (token.IsCancellationRequested)
                {
                    return;
                }

                if (queryChanged || categoryChanged)
                {
                    _fileItemMap.Clear();
                }
                else
                {
                    _currentFileOffset += tempList.Count;
                }

                _hasMoreFileResults = tempList.Count >= PageSize;

                HashSet<string> existingPaths = null!;
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    existingPaths = new HashSet<string>(FileItems.Select(f => f.FullPath), StringComparer.OrdinalIgnoreCase);
                });

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (queryChanged || categoryChanged)
                    {
                        var newPaths = new HashSet<string>(tempList.Select(f => f.FullPath), StringComparer.OrdinalIgnoreCase);

                        for (int i = FileItems.Count - 1; i >= 0; i--)
                        {
                            var path = FileItems[i].FullPath;
                            if (!newPaths.Contains(path))
                            {
                                FileItems.RemoveAt(i);
                                _fileItemMap.Remove(path);
                            }
                        }
                    }

                    foreach (var item in tempList)
                    {
                        if (existingPaths.Add(item.FullPath))
                        {
                            FileItems.Add(item);
                            _fileItemMap[item.FullPath] = item;
                        }
                    }
                });

                try
                {
                    await Parallel.ForEachAsync(tempList, new ParallelOptions
                    {
                        MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1),
                        CancellationToken = token
                    },
                    async (item, ct) =>
                    {
                        if (!_fileItemMap.TryGetValue(item.FullPath, out FileItem? target))
                        {
                            return;
                        }
                        await _thumbnailSemaphore.WaitAsync(ct);
                        try
                        {
                            var thumb = Directory.Exists(item.FullPath)
                                ? _defaultFolderIcon
                                : await thumbnailGenerator.GetThumbnailAsync(item.FullPath, 16);
                            if (thumb != null)
                                await Application.Current.Dispatcher.InvokeAsync(() => target.Thumbnail = thumb);
                        }
                        finally
                        {
                            _thumbnailSemaphore.Release();
                        }
                    });
                }
                catch (OperationCanceledException) { }
            }
            finally
            {
                _isFileLoading = false;
            }
        }

        private async void scrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_isFileLoading || _isAppLoading || _searchCts?.IsCancellationRequested == true)
            {
                return;
            }

            scrollViewer.UpdateLayout();

            if (e.ExtentHeight - e.VerticalOffset - e.ViewportHeight <= 170 && _hasMoreFileResults)
            {
                if (_searchCts != null && !_searchCts.IsCancellationRequested)
                {
                    await LoadNextFilePageAsync(_categoryFilter + _currentQuery, _searchCts.Token, false);
                }
            }
            else
            {
            }
        }
        private void ScrollSelectedIntoView()
        {
            if (FileItems_ItemsControl.ItemContainerGenerator.ContainerFromItem(_selectedItem) is not FrameworkElement container)
            {
                return;
            }
            var position = container.TransformToAncestor(scrollViewer)
                                    .Transform(new System.Windows.Point(0, 0));

            double itemTop = position.Y;
            double itemBottom = itemTop + container.ActualHeight;

            if (itemTop < 0)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + itemTop);

            }
            else if (itemBottom > scrollViewer.ViewportHeight)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + (itemBottom - scrollViewer.ViewportHeight));
            }
        }

        private void ItemTemplate_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is Border border && border.DataContext is FileItem item)
            {
                if (_darkModeApplication)
                {
                    if (item.IsSelected)
                    {
                        item.Background = (Brush)Application.Current.Resources["ControlFillColorSecondaryBrush"];
                    }
                    else
                    {
                        item.Background = (Brush)Application.Current.Resources["ControlFillColorDefaultBrush"];
                    }
                }
                else
                {
                    item.Background = (Brush)Application.Current.Resources["ControlFillColorSecondaryBrush"];
                }
            }
        }

        private void ItemTemplate_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is Border border && border.DataContext is FileItem item)
            {
                if (!item.IsSelected)
                {
                    item.Background = new SolidColorBrush(Colors.Transparent);
                }
                else
                {
                    item.Background = _darkModeApplication
                        ? (Brush)Application.Current.Resources["ControlFillColorSecondaryBrush"]
                        : (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
                }
            }
        }

        private void AppItemTemplate_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is FileItem item)
            {
                if (e.ChangedButton == MouseButton.Right)
                {
                    ContextMenu contextMenu = new ContextMenu();
                    MenuItem open = new MenuItem
                    {
                        Header = new System.Windows.Controls.TextBlock
                        {
                            Text = Lang.SearchWindow_Item_ContextMenu_Open,
                            FontSize = 13,
                            Padding = new Thickness(4, 0, 4, 0)
                        },
                        Height = 31,
                        Icon = new SymbolIcon(SymbolRegular.Open16, 16)
                    };
                    contextMenu.Items.Add(open);
                    contextMenu.IsOpen = true;
                }
            }
        }

        private void Item_MouseUp(object sender, MouseButtonEventArgs e)
        {
            FileItem item = new FileItem();
            if (sender is Border border && border.DataContext is FileItem fileitem)
            {
                item = fileitem;
            }
            else if (_selectedItem != null)
            {
                item = _selectedItem;
            }
            if (e.ChangedButton == MouseButton.Right)
            {
                ContextMenu contextMenu = new ContextMenu();
                int fontSize = 13;
                int height = 31;
                Thickness padding = new Thickness(4, 0, 4, 0);

                MenuItem open = new MenuItem
                {
                    Header = new System.Windows.Controls.TextBlock
                    {
                        Text = Lang.SearchWindow_Item_ContextMenu_Open,
                        FontSize = fontSize,
                        Padding = padding
                    },
                    Height = height,
                    Icon = new SymbolIcon(SymbolRegular.Open16, 16)
                };
                Debug.WriteLine(item.FullPath);

                open.Click += (_, _) =>
                {
                    try
                    {
                        if (!CheckNotUncPath(item.FullPath)) return;
                        Process.Start(new ProcessStartInfo(item.FullPath) { UseShellExecute = true });
                    }
                    catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException || ex is FileNotFoundException)
                    {
                        Debug.WriteLine($"Failed to open '{item.FullPath}': {ex.Message}");
                    }
                };

                MenuItem openPath = new MenuItem
                {
                    Header = new System.Windows.Controls.TextBlock
                    {
                        Text = Lang.SearchWindow_Item_ContextMenu_OpenPath,
                        FontSize = fontSize,
                        Padding = padding
                    },
                    Height = height,
                    Icon = new SymbolIcon(SymbolRegular.FolderOpen16, 16)
                };
                openPath.Click += (_, _) =>
                {
                    try
                    {
                        var dirPath = Path.GetDirectoryName(item.FullPath);
                        if (!string.IsNullOrEmpty(dirPath) && CheckNotUncPath(dirPath))
                            Process.Start(new ProcessStartInfo(dirPath) { UseShellExecute = true });
                    }
                    catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException || ex is FileNotFoundException)
                    {
                        Debug.WriteLine($"Failed to open path for '{item.FullPath}': {ex.Message}");
                    }
                };

                MenuItem copyPath = new MenuItem
                {
                    Header = new System.Windows.Controls.TextBlock
                    {
                        Text = Lang.SearchWindow_Item_ContextMenu_CopyAsPath,
                        FontSize = fontSize,
                        Padding = padding
                    },
                    Height = height,
                    Icon = new SymbolIcon(SymbolRegular.Share16, 16)
                };
                copyPath.Click += (_, _) =>
                {
                    Clipboard.SetText(item.FullPath);
                };

                MenuItem copyFolderPath = new MenuItem
                {
                    Header = new System.Windows.Controls.TextBlock
                    {
                        Text = Lang.SearchWindow_Item_ContextMenu_CopyFolderPath,
                        FontSize = fontSize,
                        Padding = padding
                    },
                    Height = height,
                    Icon = new SymbolIcon(SymbolRegular.Copy16, 16)
                };
                copyFolderPath.Click += (_, _) =>
                {
                    var dirPath = Path.GetDirectoryName(item.FullPath);
                    if (!string.IsNullOrEmpty(dirPath))
                        Clipboard.SetText(dirPath);
                };
                contextMenu.Items.Add(open);
                contextMenu.Items.Add(openPath);
                contextMenu.Items.Add(new Separator());
                contextMenu.Items.Add(copyPath);
                contextMenu.Items.Add(copyFolderPath);

                contextMenu.Items.Add(new Separator());

                // "Run as administrator" — only for .exe files
                if (!Directory.Exists(item.FullPath) &&
                    string.Equals(Path.GetExtension(item.FullPath), ".exe", StringComparison.OrdinalIgnoreCase))
                {
                    MenuItem runAsAdmin = new MenuItem
                    {
                        Header = new System.Windows.Controls.TextBlock
                        {
                            Text = Lang.SearchWindow_Item_ContextMenu_RunAsAdmin,
                            FontSize = fontSize,
                            Padding = padding
                        },
                        Height = height,
                        Icon = new SymbolIcon(SymbolRegular.ShieldCheckmark16, 16)
                    };
                    runAsAdmin.Click += (_, _) =>
                    {
                        try
                        {
                            if (!CheckNotUncPath(item.FullPath)) return;
                            System.Windows.MessageBoxResult confirm = System.Windows.MessageBox.Show(
                                $"Are you sure you want to run '{Path.GetFileName(item.FullPath)}' as administrator?",
                                "Run as Administrator",
                                System.Windows.MessageBoxButton.YesNo,
                                System.Windows.MessageBoxImage.Warning);
                            if (confirm != System.Windows.MessageBoxResult.Yes) return;
                            Process.Start(new ProcessStartInfo(item.FullPath)
                            {
                                UseShellExecute = true,
                                Verb = "runas"
                            });
                        }
                        catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException || ex is FileNotFoundException)
                        {
                            Debug.WriteLine($"Failed to run as admin '{item.FullPath}': {ex.Message}");
                        }
                    };
                    contextMenu.Items.Add(runAsAdmin);
                }

                // "Properties" — Windows shell properties dialog
                MenuItem properties = new MenuItem
                {
                    Header = new System.Windows.Controls.TextBlock
                    {
                        Text = Lang.SearchWindow_Item_ContextMenu_Properties,
                        FontSize = fontSize,
                        Padding = padding
                    },
                    Height = height,
                    Icon = new SymbolIcon(SymbolRegular.Info16, 16)
                };
                properties.Click += (_, _) =>
                {
                    try
                    {
                        if (!SHObjectProperties(IntPtr.Zero, SHOP_FILEPATH, item.FullPath, null))
                            Debug.WriteLine($"SHObjectProperties failed for '{item.FullPath}'");
                    }
                    catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException)
                    {
                        Debug.WriteLine($"Failed to show properties for '{item.FullPath}': {ex.Message}");
                    }
                };
                contextMenu.Items.Add(properties);

                contextMenu.IsOpen = true;
            }
        }

        private string HumanizeSize(long bytes)
        {
            if (bytes < 0) return "-" + HumanizeSize(bytes == long.MinValue ? long.MaxValue : -bytes);

            string[] units = { "B", "KB", "MB", "GB", "TB", "PB" };

            double size = bytes;
            int powerIndex = 0;

            while (size >= 1024 && powerIndex < units.Length - 1)
            {
                size /= 1024;
                powerIndex++;
            }
            return $"{size:0.##} {units[powerIndex]}";
        }


        /// <summary>
        /// Returns <see langword="true"/> if <paramref name="path"/> is a safe, rooted, local
        /// filesystem path that actually exists. Rejects UNC paths, device paths (\\.\, \\?\),
        /// relative paths, and non-existent paths.
        /// </summary>
        private static bool IsValidLocalPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (!Path.IsPathRooted(path)) return false;
            try
            {
                var normalized = Path.GetFullPath(path);
                if (normalized.StartsWith(@"\\", StringComparison.Ordinal)) return false;
                if (normalized.StartsWith(@"\\?\", StringComparison.Ordinal)) return false;
                if (normalized.StartsWith(@"\\.\", StringComparison.Ordinal)) return false;
                return File.Exists(normalized) || Directory.Exists(normalized);
            }
            catch { return false; }
        }

        private static bool CheckNotUncPath(string path)
        {
            if (!IsValidLocalPath(path))
            {
                System.Windows.MessageBox.Show(
                    "Opening this path is not allowed for security reasons.",
                    "Security Warning",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return false;
            }
            return true;
        }

        private static HashSet<string> ParseExtensions(string extString)
        {
            return extString
                .Replace("ext:", "")
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(e => e.Trim().ToLowerInvariant())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private async Task SetPreviewItem(FileItem item, bool showDetails)
        {
            var old = _thumbnailCts;
            old?.Cancel();
            _thumbnailCts = new CancellationTokenSource();
            old?.Dispose();
            var token = _thumbnailCts.Token;
            if (_selectedItem != null)
            {
                _selectedItem.IsSelected = false;
            }
            _selectedItem = item;
            _selectedItem.IsSelected = true;

            ScrollSelectedIntoView();

            var extension = Path.GetExtension(item.FullPath).Replace(".", "");
            bool isMedia = false;
            _selectedItemIsFile = showDetails;

            SelectedItemPreviewName.Text = item.Name;

            if (!string.IsNullOrEmpty(extension) &&
                    (ParseExtensions(SearchCategory.GetExtensions(Category.Video)).Contains(extension.ToLower()) ||
                    ParseExtensions(SearchCategory.GetExtensions(Category.Image)).Contains(extension.ToLower())
                ))
            {
                SelectedItemPreviewImage.Width = 250;
                SelectedItemPreviewImage.Height = double.NaN;
                SelectedItemPreviewImage.MaxHeight = 280;
                isMedia = true;
            }
            else
            {
                SelectedItemPreviewImage.Clip = null;
                SelectedItemPreviewImage.Width = 64;
                SelectedItemPreviewImage.Height = 64;
            }

            int thumbnailWidth = (int)(SelectedItemPreviewImage.Width * 1.2);

            try
            {
                var thumb = await thumbnailGenerator.GetThumbnailAsync(item.FullPath, thumbnailWidth);

                if (!token.IsCancellationRequested)
                {
                    SelectedItemPreviewImage.Source = thumb;
                }
            }
            catch (OperationCanceledException) { }

            bool isExe = string.Equals(Path.GetExtension(item.FullPath), ".exe", StringComparison.OrdinalIgnoreCase);
            if (Directory.Exists(item.FullPath) || !isExe) // hide option for folders and non-exe files
            {
                RunasAdminBorder.Visibility = Visibility.Collapsed;
            }
            else
            {
                RunasAdminBorder.Visibility = Visibility.Visible;
            }

            if (showDetails)
            {
                ActionSeparator.Visibility = Visibility.Visible;
            }
            else
            {
                ActionSeparator.Visibility = Visibility.Collapsed;
            }

            if (showDetails)
            {
                FilePreviewGrid.Visibility = Visibility.Visible;
                FilePreviewMPath.Text = Path.GetDirectoryName(item.FullPath);
                if (isMedia)
                {
                    RunasAdminBorder.Visibility = Visibility.Collapsed;
                    uint width = 0;
                    uint height = 0;
                    try
                    {
                        using (var shellFile = ShellFile.FromFilePath(item.FullPath))
                        {
                            if (shellFile.Properties.System.Image.HorizontalSize.Value.HasValue)
                            {
                                width = shellFile.Properties.System.Image.HorizontalSize.Value!.Value;
                                var h = shellFile.Properties.System.Image.VerticalSize.Value;
                                if (h.HasValue) height = h.Value;
                            }
                            else
                            {
                                var w = shellFile.Properties.System.Video.FrameWidth.Value;
                                var h = shellFile.Properties.System.Video.FrameHeight.Value;
                                if (w.HasValue && h.HasValue)
                                {
                                    width = w.Value;
                                    height = h.Value;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("Can't show preview:" + ex.Message);
                    }
                    if (width != 0 && height != 0)
                    {
                        FilePreviewResolution.Text = $"{width} x {height}";
                        FilePreviewResolutionTextBlock.Visibility = Visibility.Visible;
                        FilePreviewResolution.Visibility = Visibility.Visible;

                    }
                    else
                    {
                        FilePreviewResolutionTextBlock.Visibility = Visibility.Collapsed;
                        FilePreviewResolution.Visibility = Visibility.Collapsed;
                    }
                }
                else
                {
                    FilePreviewResolutionTextBlock.Visibility = Visibility.Collapsed;
                    FilePreviewResolution.Visibility = Visibility.Collapsed;
                }
                FilePreviewModDate.Text = item.ModificationDate;
                FilePreviewSize.Text = HumanizeSize(item.Size);
            }
            else
            {
                RunasAdminBorder.Visibility = isExe ? Visibility.Visible : Visibility.Collapsed;
                FilePreviewGrid.Visibility = Visibility.Collapsed;
            }

        }
        private void LoadRecentItems()
        {
            try
            {
                var raw = reg.ReadKeyValueRoot(RecentItemsRegistryKey) as string ?? string.Empty;
                _recentItems = raw
                    .Split('|', StringSplitOptions.RemoveEmptyEntries)
                    .Where(p => System.IO.File.Exists(p) || System.IO.Directory.Exists(p))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(MaxRecentItems)
                    .ToList();
            }
            catch
            {
                _recentItems = new List<string>();
            }
        }

        private void SaveRecentItems()
        {
            try
            {
                reg.WriteToRegistryRoot(RecentItemsRegistryKey, string.Join("|", _recentItems));
            }
            catch { }
        }

        private void AddRecentItem(string path)
        {
            _recentItems.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            _recentItems.Insert(0, path);
            if (_recentItems.Count > MaxRecentItems)
                _recentItems.RemoveAt(_recentItems.Count - 1);
            SaveRecentItems();
        }

        private async Task ShowRecentItemsAsync(CancellationToken token)
        {
            if (_recentItems.Count == 0) return;

            var recentFileItems = _recentItems
                .Select(path => new FileItem
                {
                    FullPath = path,
                    Name = System.IO.Path.GetFileName(path),
                    ModificationDate = System.IO.File.Exists(path)
                        ? System.IO.File.GetLastWriteTime(path).ToString()
                        : System.IO.Directory.Exists(path)
                            ? System.IO.Directory.GetLastWriteTime(path).ToString()
                            : "N/A",
                    IsRecentItem = true
                })
                .ToList();

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                FileItems.Clear();
                _fileItemMap.Clear();
                foreach (var item in recentFileItems)
                {
                    FileItems.Add(item);
                    _fileItemMap[item.FullPath] = item;
                }
            });

            try
            {
                await Parallel.ForEachAsync(recentFileItems, new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1),
                    CancellationToken = token
                },
                async (item, ct) =>
                {
                    if (!_fileItemMap.TryGetValue(item.FullPath, out FileItem? target)) return;
                    await _thumbnailSemaphore.WaitAsync(ct);
                    try
                    {
                        var thumb = System.IO.Directory.Exists(item.FullPath)
                            ? _defaultFolderIcon
                            : await thumbnailGenerator.GetThumbnailAsync(item.FullPath, 16);
                        await Application.Current.Dispatcher.InvokeAsync(() => target.Thumbnail = thumb);
                    }
                    finally
                    {
                        _thumbnailSemaphore.Release();
                    }
                });
            }
            catch (OperationCanceledException) { }
        }

        private void LoadUwpApps()        {
            using (var appsFolder = (ShellContainer)ShellObject.FromParsingName("shell:AppsFolder"))
            {
                List<string> existingUwps = new List<string>();
                string shortcutFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Everything Quick Search");
                Directory.CreateDirectory(shortcutFolder);

                // get uwp apps and create shortcuts for them if not exist, so they can be searched by everything
                foreach (var item in appsFolder)
                {
                    try
                    {
                        if (item.ParsingName.EndsWith("!App"))
                        {
                            var safeName = string.Concat(item.Name.Split(Path.GetInvalidFileNameChars()));
                            existingUwps.Add(safeName);
                            if (!File.Exists(Path.Combine(shortcutFolder, safeName + ".lnk")))
                            {
                                Debug.WriteLine("add new" + item.Name);
                                string shortcutPath = Path.Combine(shortcutFolder, safeName + ".lnk");
                                string appUserModelId = item.ParsingName;

                                var shortcut = (IWshShortcut)new WshShell().CreateShortcut(shortcutPath);

                                shortcut.TargetPath = $"shell:AppsFolder\\{item.ParsingName}";
                                shortcut.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                                shortcut.Save();
                            }
                        }
                    }
                    catch { }
                }

                foreach (var file in Directory.GetFiles(shortcutFolder, "*.lnk"))
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    if (!existingUwps.Contains(name))
                    {
                        try
                        {
                            File.Delete(file);
                            Debug.WriteLine("delete name: " + name);
                        }
                        catch
                        {
                        }
                    }
                }

            }
        }

        private async void FileItemTemplate_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is FileItem clickedItem)
            {
                if (e.ClickCount == 1)
                {
                    await SetPreviewItem(clickedItem, true);
                }
                else if (e.ClickCount == 2)
                {
                    try
                    {
                        if (!CheckNotUncPath(clickedItem.FullPath)) return;
                        Process.Start(new ProcessStartInfo(clickedItem.FullPath) { UseShellExecute = true });
                        AddRecentItem(clickedItem.FullPath);
                    }
                    catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException || ex is FileNotFoundException)
                    {
                        Debug.WriteLine($"Failed to open '{clickedItem.FullPath}': {ex.Message}");
                    }
                }
            }
        }
        private async void AppItemTemplate_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is FileItem clickedItem)
            {
                if (e.ClickCount == 1)
                {
                    await SetPreviewItem(clickedItem, false);
                }
                else if (e.ClickCount == 2)
                {
                    try
                    {
                        if (!CheckNotUncPath(clickedItem.FullPath)) return;
                        Process.Start(new ProcessStartInfo(clickedItem.FullPath) { UseShellExecute = true });
                        AddRecentItem(clickedItem.FullPath);
                    }
                    catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException || ex is FileNotFoundException)
                    {
                        Debug.WriteLine($"Failed to open '{clickedItem.FullPath}': {ex.Message}");
                    }
                }
            }
        }
        private void FluentWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Task.Run(() =>
            {
                // get animation
                SystemParametersInfo(SPI_GETCLIENTAREAANIMATION, 0, out originalAnimationState, 0);
                _originalMinAnimationState = GetMinimizeAnimation();
            });
            // Remove harsh window shadow
            var hwnd = new WindowInteropHelper(this).Handle;
            int attr = 4;
            DwmSetWindowAttribute(hwnd, 33, ref attr, sizeof(int));

            _taskbarRestartMessage = RegisterWindowMessage("TaskbarCreated");
            var hwndSource = HwndSource.FromHwnd(hwnd);
            hwndSource.AddHook(WndProc);

            ChangeSelectedButton(AllFilterButton);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == _taskbarRestartMessage)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    TrayIcon.Register();
                });
            }
            return IntPtr.Zero;
        }

        private void SelectedItemPreviewImage_MouseDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (_selectedItem != null && e.ChangedButton == MouseButton.Left)
                {
                    if (!CheckNotUncPath(_selectedItem.FullPath)) return;
                    Process.Start(new ProcessStartInfo(_selectedItem.FullPath) { UseShellExecute = true });
                }
            }
            catch { }
        }
        private void ChangeSelectedButton(Button newButton)
        {
            //  UpdateWPFUITheme();
            if (_selectedCategoryButton != null)
            {
                _selectedCategoryButton.ClearValue(Button.BackgroundProperty);
                _selectedCategoryButton.SetResourceReference(Button.ForegroundProperty, "TextFillColorPrimaryBrush");
            }
            _selectedCategoryButton = newButton;

            _selectedCategoryButton.SetResourceReference(Button.BackgroundProperty, "AccentTextFillColorTertiaryBrush");

            var newBrush = new SolidColorBrush(Colors.Transparent);

            if (_selectedCategoryButton.Background is SolidColorBrush btnBrush)
            {
                newBrush.Color = btnBrush.Color;
                Application.Current.Resources["SelectedItemBarBrush"] = newBrush;

            }
            _selectedCategoryButton.Foreground = new SolidColorBrush(_darkModeApplication ? Colors.Black : Colors.White);
        }
        private void changeRegexButtonColor()
        {
            if (enableRegex)
            {
                RegexButton.SetResourceReference(Button.BackgroundProperty, "AccentTextFillColorTertiaryBrush");
            }
            else
            {
                RegexButton.ClearValue(Button.BackgroundProperty);
                RegexButton.SetResourceReference(Button.ForegroundProperty, "TextFillColorPrimaryBrush");
            }
            RegexButton.Foreground = new SolidColorBrush(
                _darkModeApplication
                    ? (enableRegex ? Colors.Black : Colors.White)
                    : (enableRegex ? Colors.White : Colors.Black)
                );
        }
        private void RegexButton_Click(object sender, RoutedEventArgs e)
        {
            enableRegex = !enableRegex;
            if (enableRegex)
            {
                ChangeSelectedButton(AllFilterButton);
                _categoryFilter = SearchCategory.GetExtensions(Category.All);
            }
            _currentQuery = string.Empty;
            _currentFileOffset = 0;
            FileItems.Clear();
            _fileItemMap.Clear();
            SearchBarTextBox_TextChanged(SearchBarTextBox, null);
            changeRegexButtonColor();
        }
        private async void FluentWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_selectedItem == null) return;

            SearchBarTextBox.Focus();

            Key actualKey = e.Key == Key.System ? e.SystemKey : e.Key;

            // prevent altgr
            bool altDown = (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt))
                      && !((Keyboard.IsKeyDown(Key.LeftCtrl) || (Keyboard.IsKeyDown(Key.RightCtrl))));

            Key[] motionUp = { Key.W, Key.K };
            Key[] motionDown = { Key.S, Key.J };
            Key[] motionLeft = { Key.A, Key.H };
            Key[] motionRight = { Key.D, Key.L };
            int index;
            if (actualKey == Key.Escape)
            {
                this.Hide();
            }
            if (altDown)
            {
                switch (actualKey)
                {
                    case Key.R:
                        RegexButton_Click(null!, null!);
                        break;

                    case Key.D1:
                    case Key.NumPad1:
                        AllFilterButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        break;

                    case Key.D2:
                    case Key.NumPad2:
                        FilesFilterButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        break;

                    case Key.D3:
                    case Key.NumPad3:
                        FoldersFilterButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        break;

                    case Key.D4:
                    case Key.NumPad4:
                        AppsFilterButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        break;

                    case Key.D5:
                    case Key.NumPad5:
                        FilterButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        break;

                    case Key.D6:
                    case Key.NumPad6:
                        ImagesFilterButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        break;

                    case Key.D7:
                    case Key.NumPad7:
                        VideoFilterButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        break;

                    case Key.D8:
                    case Key.NumPad8:
                        CompressedFilterButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        break;
                }
            }
            if (_selectedItemIsFile)
            {
                index = FileItems.IndexOf(_selectedItem);
            }
            else
            {
                index = AppItems.IndexOf(_selectedItem);
            }
            if (actualKey == Key.PageDown)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + scrollViewer.ViewportHeight);
            }
            else if (actualKey == Key.PageUp)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - scrollViewer.ViewportHeight);
            }
            else if (actualKey == Key.End)
            {
                scrollViewer.ScrollToBottom();
                if (FileItems.Count > 0)
                {
                    await SetPreviewItem(FileItems[FileItems.Count - 1], true);
                }
                else if (FileItems.Count == 0 && AppItems.Count > 0)
                {
                    await SetPreviewItem(AppItems[AppItems.Count - 1], false);
                }
            }
            else if (actualKey == Key.Home)
            {
                scrollViewer.ScrollToTop();
                if (AppItems.Count > 0)
                {
                    await SetPreviewItem(AppItems[0], false);
                }
                if (FileItems.Count > 0 && AppItems.Count == 0)
                {
                    await SetPreviewItem(FileItems[0], true);
                }
            }
            else if (actualKey == Key.Enter)
            {
                try
                {
                    if (!CheckNotUncPath(_selectedItem.FullPath)) return;
                    Process.Start(new ProcessStartInfo(_selectedItem.FullPath) { UseShellExecute = true });
                    AddRecentItem(_selectedItem.FullPath);
                }
                catch { }
            }
            else if (actualKey == Key.Left || (altDown && motionLeft.Contains(actualKey)))
            {
                // Left arrow: move from file list to app list (first item)
                if (_selectedItemIsFile && AppItems.Count > 0)
                {
                    await SetPreviewItem(AppItems[0], false);
                }
            }
            else if (actualKey == Key.Right || (altDown && motionRight.Contains(actualKey)))
            {
                // Right arrow: move from app list to file list (first item)
                if (!_selectedItemIsFile && FileItems.Count > 0)
                {
                    await SetPreviewItem(FileItems[0], true);
                }
            }
            else if (actualKey == Key.Up || (altDown && motionUp.Contains(actualKey)))
            {
                if (_selectedItemIsFile)
                {
                    if (index != 0)
                    {
                        await SetPreviewItem(FileItems[index - 1], true);
                    }
                    else
                    {
                        if (AppItems.Count > 0)
                        {
                            await SetPreviewItem(AppItems[AppItems.Count - 1], false);
                        }
                    }
                }
                else
                {
                    if (index > 0)
                    {
                        await SetPreviewItem(AppItems[index - 1], false);
                    }
                }
            }
            else if (e.Key == Key.Down || (altDown && motionDown.Contains(actualKey)))
            {
                if (_selectedItemIsFile)
                {
                    if (index < FileItems.Count - 1)
                    {
                        await SetPreviewItem(FileItems[index + 1], true);
                    }
                }
                else
                {
                    if (index < AppItems.Count - 1)
                    {
                        await SetPreviewItem(AppItems[index + 1], false);
                    }
                    else
                    {
                        if (FileItems.Count > 0)
                        {
                            await SetPreviewItem(FileItems[0], true);
                        }
                    }
                }
            }
        }

        private void FluentWindow_PreviewKeyUp(object sender, KeyEventArgs e)
        {
        }

        private void Border_MouseEnter(object sender, MouseEventArgs e)
        {
            ((Border)sender).Background = _darkModeApplication
                ? (Brush)Application.Current.Resources["ControlFillColorDefaultBrush"]
                : (Brush)Application.Current.Resources["ControlAltFillColorTertiaryBrush"];
        }

        private void Border_MouseLeave(object sender, MouseEventArgs e)
        {
            ((Border)sender).Background = new SolidColorBrush(Colors.Transparent);
        }

        private void RunasAdminBorder_MouseDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (_selectedItem != null)
                {
                    if (!CheckNotUncPath(_selectedItem.FullPath)) return;
                    System.Windows.MessageBoxResult confirm = System.Windows.MessageBox.Show(
                        $"Are you sure you want to run '{Path.GetFileName(_selectedItem.FullPath)}' as administrator?",
                        "Run as Administrator",
                        System.Windows.MessageBoxButton.YesNo,
                        System.Windows.MessageBoxImage.Warning);
                    if (confirm != System.Windows.MessageBoxResult.Yes) return;
                    Process.Start(new ProcessStartInfo(_selectedItem.FullPath)
                    {
                        UseShellExecute = true,
                        Verb = "runas"
                    });
                }

            }
            catch (Exception ex) { Debug.WriteLine($"RunasAdmin failed: {ex.Message}"); }
        }

        private void OpenFileLocationBorder_MouseDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (_selectedItem != null)
                {
                    var dirPath = Path.GetDirectoryName(_selectedItem.FullPath);
                    if (!string.IsNullOrEmpty(dirPath) && CheckNotUncPath(dirPath))
                        Process.Start(new ProcessStartInfo(dirPath) { UseShellExecute = true });
                }
            }
            catch { }
        }

        private void OpenFileBorder_MouseDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (_selectedItem != null)
                {
                    if (!CheckNotUncPath(_selectedItem.FullPath)) return;
                    Process.Start(new ProcessStartInfo(_selectedItem.FullPath) { UseShellExecute = true });
                }
            }
            catch { }
        }

        private async void CategoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (enableRegex) return;

            if (sender is Button btn && Enum.TryParse<Category>(btn.Tag?.ToString(), out var category))
            {
                ChangeSelectedButton(btn);
                _categoryFilter = SearchCategory.GetExtensions(category);
                _currentQuery = string.Empty;
                _currentFileOffset = 0;
                FileItems.Clear();
                _fileItemMap.Clear();
                SearchBarTextBox_TextChanged(SearchBarTextBox, null);
            }
        }
        private void Populate_Sortby_DropDownButton_ContextMenu()
        {
            HashSet<String> sortValues = new HashSet<string> {
                Lang.SearchWindow_SortBy_Name,
                Lang.SearchWindow_SortBy_Path,
                Lang.SearchWindow_SortBy_Size,
                Lang.SearchWindow_SortBy_Extension,
                Lang.SearchWindow_SortBy_Type,
                Lang.SearchWindow_SortBy_DateCreated,
                Lang.SearchWindow_SortBy_DateModified,
                Lang.SearchWindow_SortBy_Attributes,
                Lang.SearchWindow_SortBy_FileListFileName,
                Lang.SearchWindow_SortBy_RunCount,
                Lang.SearchWindow_SortBy_DateRecentlyChanged,
                Lang.SearchWindow_SortBy_DateAccessed,
                Lang.SearchWindow_SortBy_DateRun};

            int sortId = 1;
            int fontSize = 13;
            int height = 31;
            Thickness padding = new Thickness(0, 0, 8, 0);

            foreach (var item in sortValues)
            {
                MenuItem menuitem = new MenuItem
                {
                    Header = new System.Windows.Controls.TextBlock
                    {
                        Text = item,
                        FontSize = fontSize,
                        Padding = padding
                    },
                    Height = height,
                    StaysOpenOnClick = true,
                    Tag = sortId,
                    Icon = new SymbolIcon { Symbol = SymbolRegular.CircleSmall20, Foreground = Brushes.Transparent, Filled = true }
                };

                if ((int)menuitem.Tag * 2 - (_setSortAscending ? 1 : 0) == _setSort)
                {
                    menuitem.Icon.SetResourceReference(Button.ForegroundProperty, "TextFillColorPrimaryBrush");
                }
                menuitem.Click += (_, _) =>
                {
                    foreach (var item in SortByContextMenu.Items)
                    {
                        if (item is MenuItem menui && menui.Header is System.Windows.Controls.TextBlock tb && sortValues.Contains(tb.Text))
                        {
                            if (menui.Icon is SymbolIcon icon)
                            {
                                icon.Foreground = Brushes.Transparent;
                            }
                        }
                    }
                    menuitem.Icon.SetResourceReference(Button.ForegroundProperty, "TextFillColorPrimaryBrush");
                    _currentSortId = (int)menuitem.Tag;
                    _setSort = (_currentSortId * 2) - (_setSortAscending ? 1 : 0);
                    _currentQuery = string.Empty;
                    SearchBarTextBox_TextChanged(SearchBarTextBox, null);

                };
                SortByContextMenu.Items.Add(menuitem);

                sortId++;
            }
            MenuItem descendingMenuItem = new MenuItem();
            MenuItem ascendingMenuItem = new MenuItem
            {
                Header = new System.Windows.Controls.TextBlock
                {
                    Text = Lang.SearchWindow_SortBy_Ascending,
                    FontSize = fontSize,
                    Padding = padding
                },
                Height = height,
                StaysOpenOnClick = true,
                Icon = new SymbolIcon { Symbol = SymbolRegular.CircleSmall20, Foreground = Brushes.Transparent, Filled = true },
            };
            ascendingMenuItem.Click += (_, _) =>
            {
                if (!_setSortAscending)
                {
                    _setSortAscending = true;
                    _setSort = (_currentSortId * 2) - 1;
                    _currentQuery = string.Empty;
                    _currentFileOffset = 0;
                    FileItems.Clear();
                    _fileItemMap.Clear();
                    SearchBarTextBox_TextChanged(SearchBarTextBox, null);

                    ascendingMenuItem.Icon.SetResourceReference(Button.ForegroundProperty, "TextFillColorPrimaryBrush");
                    descendingMenuItem.Icon.Foreground = Brushes.Transparent;
                }
            };

            descendingMenuItem = new MenuItem
            {
                Header = new System.Windows.Controls.TextBlock
                {
                    Text = Lang.SearchWindow_SortBy_Descending,
                    FontSize = fontSize,
                    Padding = padding
                },
                Height = height,
                StaysOpenOnClick = true,
                Icon = new SymbolIcon { Symbol = SymbolRegular.CircleSmall20, Foreground = Brushes.Transparent, Filled = true },
            };
            descendingMenuItem.Click += (_, _) =>
            {
                if (_setSortAscending)
                {
                    _setSortAscending = false;
                    _setSort = _currentSortId * 2;
                    _currentQuery = string.Empty;
                    _currentFileOffset = 0;
                    FileItems.Clear();
                    _fileItemMap.Clear();
                    SearchBarTextBox_TextChanged(SearchBarTextBox, null);

                    descendingMenuItem.Icon.SetResourceReference(Button.ForegroundProperty, "TextFillColorPrimaryBrush");
                    ascendingMenuItem.Icon.Foreground = Brushes.Transparent;
                }
            };

            if (_setSortAscending)
            {
                ascendingMenuItem.Icon.SetResourceReference(Button.ForegroundProperty, "TextFillColorPrimaryBrush");
                descendingMenuItem.Icon.Foreground = Brushes.Transparent;
            }
            else
            {
                descendingMenuItem.SetResourceReference(Button.BackgroundProperty, "TextFillColorPrimaryBrush");
                ascendingMenuItem.Icon.Foreground = Brushes.Transparent;
            }

            SortByContextMenu.Items.Add(new Separator());
            SortByContextMenu.Items.Add(ascendingMenuItem);
            SortByContextMenu.Items.Add(descendingMenuItem);
        }

        private void FilterButton_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is Button btn)
            {
                if (enableRegex)
                {
                    btn.ToolTip = Lang.SearchWindow_FilterButton_ToolTip_RegexEnabled;
                }
                else
                {
                    btn.ToolTip = null;
                }
            }
        }
        private void FluentWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_hookForeground != IntPtr.Zero)
            {
                UnhookWinEvent(_hookForeground);
                _hookForeground = IntPtr.Zero;
            }
            if (_winEventDelegateHandle.IsAllocated)
            {
                _winEventDelegateHandle.Free();
            }
            if (_everything != null)
            {
                _everything.Dispose();
                _everything = null;
            }
            m_GlobalHook.KeyPress -= GlobalHookKeyPress;
            m_GlobalHook.KeyDown -= M_GlobalHook_KeyDown;
            m_GlobalHook.Dispose();

            _thumbnailSemaphore.Dispose();
            _debounceCts?.Dispose();
            _searchCts?.Dispose();
            _thumbnailCts?.Dispose();

            // Restore system animations in case they were disabled
            try
            {
                SystemParametersInfo(SPI_SETCLIENTAREAANIMATION, 0, originalAnimationState, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
                SetMinimizeAnimation(_originalMinAnimationState);
            }
            catch { }
        }

        private void FileItemTemplate_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && sender is Border border)
            {
                if (border.DataContext is FileItem fileItem)
                {
                    var safePath = SanitizePathForDragDrop(fileItem.FullPath!);
                    DataObject data = new DataObject(DataFormats.FileDrop, new string[] { safePath });
                    DragDrop.DoDragDrop(border, data, DragDropEffects.Copy | DragDropEffects.Move);
                }
            }
        }

        /// <summary>
        /// Strips any NTFS alternate data stream suffix (e.g. ":streamname") from a path before
        /// passing it to drag-and-drop or shell operations.
        /// </summary>
        private static string SanitizePathForDragDrop(string path)
        {
            // Skip index 0 (start of path) and 1 (drive letter colon); look for a second colon.
            int colonIndex = path.IndexOf(':', 2);
            return colonIndex >= 0 ? path.Substring(0, colonIndex) : path;
        }
        private void RoundedButton_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is DropDownButton btn)
            {
                btn.Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
            }
            else if (sender is Button btn2)
            {
                btn2.Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
            }
        }

        private void RoundedButton_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is DropDownButton btn)
            {
                btn.Background = Brushes.Transparent;
            }
            else if (sender is Button btn2)
            {
                btn2.Background = Brushes.Transparent;
            }
        }

        private void SettingsButtons_Click(object sender, RoutedEventArgs e)
        {
            new SettingsWindow(this).Show();
        }

        #region notifyicon
        private void AutorunToggle_CheckChanged(object sender, RoutedEventArgs e)
        {
            if ((bool)AutorunToggle.IsChecked!)
            {
                string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath))
                    reg.AddToAutoRun("EverythingQuickSearch", exePath);
            }
            else
            {
                reg.RemoveFromAutoRun("EverythingQuickSearch");
            }
            reg.WriteToRegistryRoot("startOnLogin", AutorunToggle.IsChecked);
        }

        private void visitGithub_Buton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ProcessStartInfo sInfo = new ProcessStartInfo($"https://github.com/arunabhobasu/EverythingToolbarSearch") { UseShellExecute = true };
                _ = Process.Start(sInfo);
            }
            catch { }
        }
        private void ExitApp(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        #endregion


    }
}