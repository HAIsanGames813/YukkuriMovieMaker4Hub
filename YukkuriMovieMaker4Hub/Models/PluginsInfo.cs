using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace YukkuriMovieMaker4Hub
{
    // プラグインディレクトリ直下のinfo.jsonに保存する統合プラグイン情報
    public class PluginsInfo
    {
        [JsonPropertyName("plugins")]
        public List<PluginMetadata> Plugins { get; set; } = new List<PluginMetadata>();
    }

    public class PluginMetadata
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("portalName")]
        public string PortalName { get; set; } = string.Empty;

        [JsonPropertyName("author")]
        public string Author { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("owner")]
        public string Owner { get; set; } = string.Empty;

        [JsonPropertyName("repo")]
        public string Repo { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("publishedAt")]
        public DateTime? PublishedAt { get; set; }

        [JsonPropertyName("downloadedAt")]
        public DateTime? DownloadedAt { get; set; }
    }
}
