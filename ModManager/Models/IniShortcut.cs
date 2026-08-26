namespace ModManager.Models
{
    public class IniShortcut
    {
        public string Key { get; set; }
        public string IniFileName { get; set; }
        public string Section { get; set; }
        public string ShortcutValue { get; set; }
        public int Value { get; set; }
        public int OptionIndex { get; set; }
    }
}
