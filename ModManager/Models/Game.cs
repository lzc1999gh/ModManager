using System;

namespace WpfApp1.Models
{
    public class Game
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        // Per-game mods root path (user-configurable)
        public string ModsRootPath { get; set; }
    }
}
