using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ModManager.Converters
{
    /// <summary>
    /// 路径 → ImageSource。带解码缓存，避免列表容器重建时反复读盘 + 重复解码。
    ///
    /// 缓存键包含文件的写入时间与长度，所以"修改头像"这类覆盖同名文件的操作
    /// 也会自然失效，不会显示旧图。
    ///
    /// ConverterParameter 可传一个正整数作为缩略图解码宽度（如 "128"），
    /// 此时按该宽度解码（大幅降低内存与解码耗时）；不传则按原图解码。
    /// </summary>
    public class ImagePathToImageSourceConverter : IValueConverter
    {
        // 缩略图缓存：128px 级别的位图约 64KB/张，512 张约 32MB 上限
        private const int ThumbnailCacheCapacity = 512;
        // 原图缓存：预览图可能很大，控制条数避免内存膨胀
        private const int FullSizeCacheCapacity = 16;

        private static readonly LruCache ThumbnailCache = new(ThumbnailCacheCapacity);
        private static readonly LruCache FullSizeCache = new(FullSizeCacheCapacity);

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            try
            {
                if (value is not string raw || string.IsNullOrWhiteSpace(raw))
                    return null;

                // 去掉可能存在的引号
                var path = raw.Trim().Trim('"');

                // 如果是相对路径，转换成绝对路径
                if (!Path.IsPathRooted(path))
                {
                    path = Path.GetFullPath(path);
                }

                // SVG：WPF 原生 BitmapImage 不支持 SVG，尝试读取同名 PNG
                if (string.Equals(Path.GetExtension(path), ".svg", StringComparison.OrdinalIgnoreCase))
                {
                    path = Path.ChangeExtension(path, ".png");
                }

                // 一次 stat 同时拿到"是否存在 + 写入时间 + 长度"，用于缓存键
                var file = new FileInfo(path);
                if (!file.Exists) return null;

                var decodeWidth = ParseDecodeWidth(parameter);
                var cache = decodeWidth > 0 ? ThumbnailCache : FullSizeCache;
                var key = decodeWidth + "|" + file.Length + "|" + file.LastWriteTimeUtc.Ticks + "|" + path;

                if (cache.TryGet(key, out var cached)) return cached;

                // 读取到内存，避免文件被 BitmapImage 锁定
                var bytes = File.ReadAllBytes(path);

                using (var ms = new MemoryStream(bytes))
                {
                    var bitmap = new BitmapImage();

                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    if (decodeWidth > 0) bitmap.DecodePixelWidth = decodeWidth;
                    bitmap.StreamSource = ms;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    cache.Set(key, bitmap);
                    return bitmap;
                }
            }
            catch
            {
                return null;
            }
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        private static int ParseDecodeWidth(object? parameter)
        {
            switch (parameter)
            {
                case int width when width > 0:
                    return width;
                case string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0:
                    return parsed;
                default:
                    return 0;
            }
        }

        /// <summary>定容量的最近最少使用缓存，线程安全。</summary>
        private sealed class LruCache
        {
            private readonly int _capacity;
            private readonly Dictionary<string, Entry> _entries;
            private readonly LinkedList<string> _order = new();

            public LruCache(int capacity)
            {
                _capacity = capacity;
                _entries = new Dictionary<string, Entry>(capacity, StringComparer.OrdinalIgnoreCase);
            }

            public bool TryGet(string key, out ImageSource image)
            {
                lock (_order)
                {
                    if (_entries.TryGetValue(key, out var entry))
                    {
                        _order.Remove(entry.Node);
                        _order.AddFirst(entry.Node);
                        image = entry.Image;
                        return true;
                    }
                }

                image = null!;
                return false;
            }

            public void Set(string key, ImageSource image)
            {
                lock (_order)
                {
                    if (_entries.ContainsKey(key)) return;

                    var node = _order.AddFirst(key);
                    _entries[key] = new Entry(image, node);

                    while (_entries.Count > _capacity && _order.Last != null)
                    {
                        var oldest = _order.Last.Value;
                        _order.RemoveLast();
                        _entries.Remove(oldest);
                    }
                }
            }

            private readonly record struct Entry(ImageSource Image, LinkedListNode<string> Node);
        }
    }
}
