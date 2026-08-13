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
                if (string.IsNullOrEmpty(path)) return null;
                if (!File.Exists(path)) return null;
                // 先尝试以字节读取到内存流，避免文件锁定问题
                var ext = Path.GetExtension(path)?.ToLowerInvariant();
                if (ext == ".svg")
                {
                    // try fallback: same-name .png next to svg
                    var png = Path.ChangeExtension(path, ".png");
                    if (File.Exists(png))
                    {
                        var bytesP = File.ReadAllBytes(png);
                        using (var msP = new MemoryStream(bytesP))
                        {
                            var bmpP = new BitmapImage();
                            bmpP.BeginInit();
                            bmpP.CacheOption = BitmapCacheOption.OnLoad;
                            bmpP.StreamSource = msP;
                            bmpP.EndInit();
                            bmpP.Freeze();
                            return bmpP;
                        }
                    }
                    // no raster fallback found
                    return null;
                }

                var bytes = File.ReadAllBytes(path);
                using (var ms = new MemoryStream(bytes))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    bmp.Freeze();
                    return bmp;
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
