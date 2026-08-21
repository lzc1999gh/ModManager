namespace ModManager.Models
{
    /// <summary>
    /// 角色信息文件中的最小角色定义。头像是独立资源，可以不存在。
    /// </summary>
    public class CharacterInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }
}
