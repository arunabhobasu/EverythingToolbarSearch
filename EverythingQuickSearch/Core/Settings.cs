using System.ComponentModel;
using EverythingQuickSearch.Util;

namespace EverythingQuickSearch.Core
{
    public class Settings : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private RegistryHelper _regHelper;
        private bool _transparentBackground;
        private int _pageSize;
        private int _defaultSort;
        private bool _enableRegexByDefault;
        private double _windowOpacity;

        public bool TransparentBackground
        {
            get => _transparentBackground;
            set
            {
                if (_transparentBackground != value)
                {
                    _transparentBackground = value;
                    OnPropertyChanged(nameof(TransparentBackground), value);
                }
            }
        }

        public int PageSize
        {
            get => _pageSize;
            set
            {
                if (_pageSize != value)
                {
                    _pageSize = value;
                    OnPropertyChanged(nameof(PageSize), value);
                }
            }
        }

        public int DefaultSort
        {
            get => _defaultSort;
            set
            {
                if (_defaultSort != value)
                {
                    _defaultSort = value;
                    OnPropertyChanged(nameof(DefaultSort), value);
                }
            }
        }

        public bool EnableRegexByDefault
        {
            get => _enableRegexByDefault;
            set
            {
                if (_enableRegexByDefault != value)
                {
                    _enableRegexByDefault = value;
                    OnPropertyChanged(nameof(EnableRegexByDefault), value);
                }
            }
        }

        public double WindowOpacity
        {
            get => _windowOpacity;
            set
            {
                if (_windowOpacity != value)
                {
                    _windowOpacity = value;
                    OnPropertyChanged(nameof(WindowOpacity), value);
                }
            }
        }

        protected void OnPropertyChanged(string propertyName, object value)
        {
            _regHelper.WriteToRegistryRoot(propertyName, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public Settings()
        {
            _regHelper = new RegistryHelper("EverythingQuickSearch");

            _transparentBackground = _regHelper.ReadKeyValueRootBool("TransparentBackground");

            var ps = _regHelper.ReadKeyValueRootInt("PageSize");
            _pageSize = ps is int pi ? Math.Clamp(pi, 5, 200) : 30;

            var ds = _regHelper.ReadKeyValueRootInt("DefaultSort");
            _defaultSort = ds is int di ? Math.Clamp(di, 1, 26) : 1;

            _enableRegexByDefault = _regHelper.ReadKeyValueRootBool("EnableRegexByDefault");

            var wo = _regHelper.ReadKeyValueRootDouble("WindowOpacity");
            _windowOpacity = wo is double wd ? Math.Clamp(wd, 0.5, 1.0) : 0.6;
        }
    }
}
