using System;
using System.Collections.Generic;

namespace ModManager.Services
{
    /// <summary>
    /// 主角名称归一。
    ///
    /// 官方图鉴会把主角按元素 / 性别拆成多条（原神 7 个「旅行者·X」、鸣潮 8 个
    /// 「漂泊者-男/女-X」）。这些条目在同步时会被当成互不相同的角色全部写进角色表，
    /// 界面上就出现多个主角。此处把同源条目折叠回一个名称，让后续按名称去重生效。
    ///
    /// 匹配必须用前缀（StartsWith）：原神另有「流浪者」（散兵）以及
    /// 「奇偶·男性 / 奇偶·女性」，与「旅行者」字形接近，用 Contains 会误伤。
    /// </summary>
    public static class ProtagonistAliases
    {
        /// <summary>游戏 Id → 主角的归一名。</summary>
        private static readonly Dictionary<string, string> CanonicalNames =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["GI"] = "旅行者",
                ["WW"] = "漂泊者",
            };

        /// <summary>取该游戏主角的归一名；没有配置主角规则时返回 null。</summary>
        public static string? TryGetCanonicalName(string? gameId)
        {
            if (string.IsNullOrWhiteSpace(gameId)) return null;
            return CanonicalNames.TryGetValue(gameId.Trim(), out var canonicalName)
                ? canonicalName
                : null;
        }

        /// <summary>把图鉴条目名称归一为主角名；不是主角条目则原样返回。</summary>
        public static string Normalize(string? gameId, string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return name ?? string.Empty;

            var canonicalName = TryGetCanonicalName(gameId);
            if (canonicalName == null) return name;

            var trimmed = name.Trim();
            return trimmed.StartsWith(canonicalName, StringComparison.OrdinalIgnoreCase)
                ? canonicalName
                : trimmed;
        }
    }
}
