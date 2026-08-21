using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace ModManager.Converters
{
    public class ImagePathToImageSourceConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                var path = value as string;

                if (string.IsNullOrWhiteSpace(path))
                    return null;

                // 去掉可能存在的引号
                path = path.Trim().Trim('"');

                // 如果是相对路径，转换成绝对路径
                if (!Path.IsPathRooted(path))
                {
                    path = Path.GetFullPath(path);
                }

                if (!File.Exists(path))
                    return null;

                var ext = Path.GetExtension(path)?.ToLowerInvariant();

                // SVG：WPF 原生 BitmapImage 不支持 SVG
                // 尝试读取同名 PNG
                if (ext == ".svg")
                {
                    var pngPath = Path.ChangeExtension(path, ".png");

                    if (!File.Exists(pngPath))
                        return null;

                    path = pngPath;
                }

                // 读取到内存，避免文件被 BitmapImage 锁定
                var bytes = File.ReadAllBytes(path);

                using (var ms = new MemoryStream(bytes))
                {
                    var bitmap = new BitmapImage();

                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = ms;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    return bitmap;
                }
            }
            catch
            {
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}