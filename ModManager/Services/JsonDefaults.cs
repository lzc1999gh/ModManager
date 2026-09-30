using System.Text.Encodings.Web;
using System.Text.Json;

namespace ModManager.Services
{
    /// <summary>统一的 JSON 序列化配置（游戏配置、角色信息、状态快照共用）。</summary>
    public static class JsonDefaults
    {
        public static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
    }
}
