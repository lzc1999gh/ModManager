namespace ModManager.Models
{
    public class IniShortcut
    {
        public string Key { get; set; } = string.Empty;
        public string IniFileName { get; set; } = string.Empty;
        public string Section { get; set; } = string.Empty;
        public string ShortcutValue { get; set; } = string.Empty;
        public int Value { get; set; }
        public int OptionIndex { get; set; }
    }
}
