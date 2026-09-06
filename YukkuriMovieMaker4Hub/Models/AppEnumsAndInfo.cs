namespace YukkuriMovieMaker4Hub
{
    public enum AppTheme
    {
        Windows,
        Light,
        Dark,
        Black
    }

    public enum PluginLocalStatus
    {
        NotInstalled,
        UpToDate,
        HasUpdate
    }

    public class LanguageInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
    }

    public class HubInfo
    {
        public string PortalName { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Owner { get; set; } = string.Empty;
        public string Repo { get; set; } = string.Empty;
    }

    public class YmmUpdateItem
    {
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ArticleUrl { get; set; } = string.Empty;
        public System.Version? Version { get; set; }
        public System.DateTime PublishedAt { get; set; }
    }
}
