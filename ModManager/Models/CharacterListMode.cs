namespace ModManager.Models
{
    /// <summary>角色列表的显示范围（右上角三个圆点按钮切换）。</summary>
    public enum CharacterListMode
    {
        /// <summary>默认列表：不包含被隐藏的角色。</summary>
        Default,

        /// <summary>仅显示带有 Mod 的角色；被隐藏的角色同样不显示。</summary>
        WithMods,

        /// <summary>全部列表：包含被隐藏的角色。</summary>
        All,
    }
}
