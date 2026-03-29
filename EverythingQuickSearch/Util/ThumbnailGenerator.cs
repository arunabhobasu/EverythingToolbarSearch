using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Shell;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace EverythingQuickSearch
{
    internal sealed class ThumbnailGenerator
    {
        private const int ThumbnailCacheMaxSize = 500;
        private readonly ConcurrentDictionary<string, BitmapSource> _cache = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentQueue<string> _cacheOrder = new();

        private string CacheKey(string path, int size) => $"{path}:{size}";

        private BitmapSource? GetFromCache(string path, int size)
        {
            _cache.TryGetValue(CacheKey(path, size), out var result);
            return result;
        }

        private void AddToCache(string path, int size, BitmapSource bitmap)
        {
            string key = CacheKey(path, size);
            if (_cache.TryAdd(key, bitmap))
            {
                _cacheOrder.Enqueue(key);
                while (_cache.Count > ThumbnailCacheMaxSize && _cacheOrder.TryDequeue(out string? oldest))
                {
                    _cache.TryRemove(oldest, out _);
                }
            }
        }

        public BitmapSource? GetThumbnail(string filePath, int size)
        {
            try
            {
                using ShellObject shellObject = ShellObject.FromParsingName(filePath);
                ShellThumbnail shellThumbnail = shellObject.Thumbnail;
                shellThumbnail.CurrentSize = new System.Windows.Size(size, size);
                BitmapSource thumbnail = shellThumbnail.BitmapSource;
                thumbnail.Freeze();
                return thumbnail;
            }
            catch (Exception e)
            {
                Debug.WriteLine($"GetThumbnail: {e.Message}");
                return null;
            }
        }

        public async Task<BitmapSource?> GetThumbnailAsync(string path, int Iconsize)
        {
            int iconSize = Iconsize;

            var cached = GetFromCache(path, iconSize);
            if (cached != null)
                return cached;

            return await Task.Run(async () =>
            {
                if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path)))
                {
                    Debug.WriteLine("Invalid path: " + path);
                    var emptyDocument = Imaging.CreateBitmapSourceFromHIcon(
                        SystemIcons.GetStockIcon(StockIconId.DocumentNoAssociation, iconSize).Handle,
                        Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(iconSize, iconSize));
                    emptyDocument.Freeze();

                    return emptyDocument;
                }
                IntPtr hBitmap = IntPtr.Zero;
                BitmapSource? thumbnail = null;
                if (Path.GetExtension(path).ToLower() == ".svg")
                {
                    try
                    {
                        thumbnail = await LoadSvgThumbnailAsync(path, iconSize);
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine(e);
                    }
                    if (thumbnail != null) AddToCache(path, iconSize, thumbnail);
                    return thumbnail;
                }
                string ext = Path.GetExtension(path).ToLowerInvariant();
                bool isLink = ext == ".lnk" || ext == ".url";

                if (isLink)
                {
                    try
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            thumbnail = GetThumbnail(path, Iconsize);
                        });
                        if (thumbnail != null) AddToCache(path, iconSize, thumbnail);
                        return thumbnail;
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine(e);
                    }
                }
                else
                {
                    try
                    {
                        int attempt = 0;
                        while (attempt < 3 && thumbnail == null)
                        {
                            ShellObject? shellObj = null;
                            shellObj = Directory.Exists(path) ? ShellObject.FromParsingName(path) : ShellFile.FromFilePath(path);
                            if (shellObj != null)
                            {
                                try
                                {
                                    Application.Current.Dispatcher.Invoke(() =>
                                    {
                                        thumbnail = GetThumbnail(path, iconSize);
                                    });
                                    if (thumbnail != null)
                                    {
                                        AddToCache(path, iconSize, thumbnail);
                                        return thumbnail;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine("Failed to fetch thumbnail:" + ex.Message);
                                }
                                finally
                                {
                                    shellObj?.Dispose();
                                }
                            }
                            attempt++;
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine(e);
                    }
                }
                if (thumbnail != null)
                {
                    return thumbnail;
                }

                Debug.WriteLine("Failed to retrieve thumbnail after 3 attempts.");
                var fallback = Imaging.CreateBitmapSourceFromHIcon(
                    SystemIcons.GetStockIcon(StockIconId.DocumentNoAssociation, iconSize).Handle,
                    Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(iconSize, iconSize));
                fallback.Freeze();

                return fallback;
            });
        }


        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int ExtractIconEx(string lpszFile, int nIconIndex, IntPtr[] phiconLarge, IntPtr[]? phiconSmall, int nIcons);


        private async Task<BitmapSource?> LoadSvgThumbnailAsync(string path, int iconSize)
        {
            try
            {
                var svgDocument = Svg.SvgDocument.Open(path);

                using (var bitmap = svgDocument.Draw(iconSize, iconSize))
                {
                    using (var ms = new MemoryStream())
                    {
                        if (bitmap == null)
                        {
                            return null;
                        }
                        bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        ms.Seek(0, SeekOrigin.Begin);

                        BitmapImage? bitmapImage = null;
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            bitmapImage = new BitmapImage();
                            bitmapImage.BeginInit();
                            bitmapImage.StreamSource = ms;
                            bitmapImage.DecodePixelWidth = 64;
                            bitmapImage.DecodePixelHeight = 64;
                            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                            bitmapImage.EndInit();
                        });
                        return bitmapImage;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load SVG thumbnail: {ex.Message}");
                return null;
            }
        }

    }
}
