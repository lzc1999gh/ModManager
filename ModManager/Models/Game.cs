namespace ModManager.Models
{
    public class Game
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        // 每个游戏独立的 Mods 根目录路径（用户可配置）
        public string ModsRootPath { get; set; }
    }
}
