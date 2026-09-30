using System;

namespace ModManager.Services
{
    /// <summary>
    /// Mod 名称与 DISABLED_ 前缀的统一处理。
    /// 原先该逻辑散落在扫描、启用/禁用、重命名等多处，现集中于此。
    /// </summary>
    public static class ModNaming
    {
        public const string DisabledPrefix = "DISABLED_";

        /// <summary>名称是否带禁用前缀。</summary>
        public static bool IsDisabled(string? name) =>
            !string.IsNullOrEmpty(name) && name.StartsWith(DisabledPrefix, StringComparison.OrdinalIgnoreCase);

        /// <summary>去掉禁用前缀后的显示名。</summary>
        public static string ToDisplayName(string? name) =>
            IsDisabled(name) ? name!.Substring(DisabledPrefix.Length) : name ?? string.Empty;

        /// <summary>加上禁用前缀（已有前缀则原样返回）。</summary>
        public static string WithDisabledPrefix(string name) =>
            IsDisabled(name) ? name : DisabledPrefix + name;
    }
}
