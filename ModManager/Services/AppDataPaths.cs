using System;
using System.IO;

namespace ModManager.Services
{
    internal static class AppDataPaths
    {
        // 使用程序目录下的 Data，实现便携式配置。配置与程序一起迁移或删除。
        public static string DataDirectory => Path.Combine(AppContext.BaseDirectory, "Data");

        // 旧版本使用的用户配置目录，仅用于首次启动时迁移。
        public static string LegacyDataDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModManager");
    }
}
