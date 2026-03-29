using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace EverythingQuickSearch
{
    /// <summary>
    /// Represents a single file or folder result returned from an Everything search query.
    /// Implements <see cref="INotifyPropertyChanged"/> for WPF data-binding.
    /// </summary>
    public class FileItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            try
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
            catch { }
        }
        private Brush _background = Brushes.Transparent;
        private string _name = string.Empty;
        private string _fullPath = string.Empty;
        private long _size = 0;
        private ImageSource? _thumbnail;
        private string? _modificationDate;
        private bool _isSelected;

        public bool IsFolder { get; set; }
        public bool IsRecentItem { get; set; }
        public string Name
        {
            get => _name;
            set
            {
                if (_name == value) return;
                _name = value;
                OnPropertyChanged(nameof(Name));
            }
        }

        public string FullPath
        {
            get => _fullPath;
            set
            {
                if (_fullPath == value) return;
                _fullPath = value;
                OnPropertyChanged(nameof(FullPath));
            }
        }
        public long Size
        {
            get => _size;
            set
            {
                if (_size == value) return;
                _size = value;
                OnPropertyChanged(nameof(Size));
            }
        }

        public string? ModificationDate
        {
            get => _modificationDate ?? string.Empty;
            set
            {
                if (_modificationDate == value) return;
                _modificationDate = value;
                OnPropertyChanged(nameof(ModificationDate));
            }
        }

        public ImageSource? Thumbnail
        {
            get => _thumbnail;
            set
            {
                if (_thumbnail == value) return;
                _thumbnail = value;
                OnPropertyChanged(nameof(Thumbnail));
            }
        }
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;

                    // dark and light theming
                    if (_isSelected)
                    {
                        if (Background == (Brush)Application.Current.Resources["ControlFillColorDefaultBrush"])
                        {
                            Background = (Brush)Application.Current.Resources["ControlFillColorSecondaryBrush"];
                        }
                        else
                        {
                            Background = (Brush)Application.Current.Resources["ControlFillColorDefaultBrush"];
                        }
                    }
                    else
                    {
                        Background = Brushes.Transparent;
                    }

                    OnPropertyChanged(nameof(IsSelected));
                    OnPropertyChanged(nameof(Background));

                }
            }
        }
        public Brush Background
        {
            get => _background;
            set
            {
                _background = value;
                OnPropertyChanged(nameof(Background));
            }
        }


    }
}
