using ModManager.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;

namespace ModManager.Services
{
    /// <summary>
    /// 从官方图鉴页面读取角色名称和头像地址。
    /// 官方页面的角色列表由 JavaScript 动态加载，因此由随应用发布的 Python
    /// 脚本调用本机 Chrome/Edge 渲染页面，再把结果以 JSON 返回给应用。
    /// </summary>
    public sealed class CharacterInfoSyncService
    {
        private const string GenshinGameId = "GI";
        private const string WutheringWavesGameId = "WW";
        private const int CrawlerTimeoutSeconds = 90;
        private const string CrawlerScriptRelativePath = "Tools\\character_info_crawler.py";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };
        private readonly HttpClient _httpClient;

        public CharacterInfoSyncService()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.All
            };
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "ModManager/1.0 (+https://github.com/lzc1999gh/ModManager) Mozilla/5.0");
            _httpClient.DefaultRequestHeaders.Accept.ParseAdd(
                "image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8");
            _httpClient.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9");
        }

        public bool Supports(Game game)
        {
            return TryGetGameId(game, out _);
        }

        public async Task<IReadOnlyList<SyncedCharacterInfo>> FetchCharacterInfosAsync(
            Game game,
            CancellationToken cancellationToken = default)
        {
            if (!TryGetGameId(game, out var gameId))
                throw new NotSupportedException("当前游戏没有配置可用的官方角色图鉴。");

            var scriptPath = Path.Combine(AppContext.BaseDirectory, CrawlerScriptRelativePath);
            if (!File.Exists(scriptPath))
            {
                throw new InvalidOperationException(
                    $"未找到角色图鉴抓取脚本：{scriptPath}。请重新生成或发布应用。 ");
            }

            var pythonPath = FindPythonPath();
            if (pythonPath == null)
            {
                throw new InvalidOperationException(
                    "未找到 Python。请安装 Python 3，或设置 MODMANAGER_PYTHON 环境变量后重试。 ");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = pythonPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            startInfo.ArgumentList.Add(scriptPath);
            startInfo.ArgumentList.Add("--game");
            startInfo.ArgumentList.Add(gameId);
            startInfo.ArgumentList.Add("--timeout-seconds");
            startInfo.ArgumentList.Add(CrawlerTimeoutSeconds.ToString());

            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            try
            {
                if (!process.Start())
                    throw new InvalidOperationException("无法启动 Python 角色图鉴抓取脚本。");
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                throw new InvalidOperationException($"无法启动 Python：{ex.Message}", ex);
            }

            using var cancellationRegistration = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch { }
            });

            var standardOutputTask = process.StandardOutput.ReadToEndAsync();
            var standardErrorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(cancellationToken);
            var output = await standardOutputTask;
            var error = (await standardErrorTask).Trim();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(error)
                        ? "Python 角色图鉴抓取失败。"
                        : error);
            }

            CrawlerResult? result;
            try
            {
                result = JsonSerializer.Deserialize<CrawlerResult>(output, JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("Python 返回的角色图鉴数据不是有效 JSON。", ex);
            }

            var characters = result?.Characters?
                .Where(character => !string.IsNullOrWhiteSpace(character.Name))
                .Select(character => new SyncedCharacterInfo(
                    character.Name.Trim(),
                    character.ImageUrl?.Trim() ?? string.Empty))
                .GroupBy(character => character.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList() ?? new List<SyncedCharacterInfo>();
            if (characters.Count == 0)
                throw new InvalidOperationException("图鉴页面未返回可识别的角色信息，可能是页面结构已变化。");

            return characters;
        }

        public async Task DownloadAvatarAsync(string imageUrl, string destinationPath, CancellationToken cancellationToken = default)
        {
            if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https"))
                throw new InvalidDataException("图鉴没有返回有效的头像地址。");

            var bytes = await _httpClient.GetByteArrayAsync(uri, cancellationToken);
            if (bytes.Length == 0)
                throw new InvalidDataException("图鉴头像为空。");

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            var temporaryPath = destinationPath + ".tmp";
            await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken);
            File.Move(temporaryPath, destinationPath, true);
        }

        private static bool TryGetGameId(Game? game, out string gameId)
        {
            var candidate = game?.Id?.Trim();
            if (!string.Equals(candidate, GenshinGameId, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(candidate, WutheringWavesGameId, StringComparison.OrdinalIgnoreCase))
            {
                gameId = string.Empty;
                return false;
            }

            gameId = candidate!;
            return true;
        }

        private static string? FindPythonPath()
        {
            var configuredPath = Environment.GetEnvironmentVariable("MODMANAGER_PYTHON")?.Trim().Trim('"');
            if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
                return configuredPath;

            var candidates = new List<string>
            {
                Path.Combine(AppContext.BaseDirectory, "python.exe"),
                @"D:\miniconda3\python.exe",
                @"C:\ProgramData\miniconda3\python.exe",
                @"C:\ProgramData\Anaconda3\python.exe"
            };

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            candidates.Add(Path.Combine(localAppData, "Programs\\Python\\Python313\\python.exe"));
            candidates.Add(Path.Combine(localAppData, "Programs\\Python\\Python312\\python.exe"));

            var pathValue = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrWhiteSpace(pathValue))
            {
                foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                {
                    candidates.Add(Path.Combine(directory.Trim().Trim('"'), "python.exe"));
                    candidates.Add(Path.Combine(directory.Trim().Trim('"'), "py.exe"));
                }
            }

            foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }

            return null;
        }

        private sealed class CrawlerResult
        {
            public List<CrawlerCharacter> Characters { get; set; } = new();
        }

        private sealed class CrawlerCharacter
        {
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("image_url")]
            public string ImageUrl { get; set; } = string.Empty;
        }

        public sealed record SyncedCharacterInfo(string Name, string ImageUrl);
    }
}
