using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace YukkuriMovieMaker4Hub
{
    public class AppSettings
    {
        [JsonPropertyName("fontFamily")]
        public string FontFamily { get; set; } = "Segoe UI";
        [JsonPropertyName("theme")]
        public AppTheme Theme { get; set; } = AppTheme.Windows;
        [JsonPropertyName("instances")]
        public List<InstanceInfo> Instances { get; set; } = new List<InstanceInfo>();
        [JsonPropertyName("projectDirectories")]
        public List<string> ProjectDirectories { get; set; } = new List<string>();
        [JsonPropertyName("closeOnLaunch")]
        public bool CloseOnLaunch { get; set; } = false;
        [JsonPropertyName("lastSelectedInstanceId")]
        public string? LastSelectedInstanceId { get; set; }
        [JsonPropertyName("languageCode")]
        public string LanguageCode { get; set; } = "ja-JP";
        [JsonPropertyName("itemSize")]
        public double ItemSize { get; set; } = 300;
        [JsonPropertyName("instancePanelWidth")]
        public double InstancePanelWidth { get; set; } = 200;
        [JsonPropertyName("hideExePath")]
        public bool HideExePath { get; set; } = false;
        public bool AutoOpenSiteOnBulkDownload { get; set; } = true;
        public bool IsViewTile { get; set; } = true;
        public List<string> ExcludeDirectories { get; set; } = new List<string>();
        [JsonPropertyName("ignoreHubUpdateTag")]
        public string? IgnoreHubUpdateTag { get; set; }
    }
}
