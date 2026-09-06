using System;

namespace YukkuriMovieMaker4Hub
{
    public class PluginLink
    {
        public string Url { get; set; } = string.Empty;
        public string Label => GetLabel(Url);
        public string FaviconUrl => string.IsNullOrEmpty(Url) ? string.Empty : $"https://www.google.com/s2/favicons?domain={new Uri(Url).Host}&sz=32";
        private string GetLabel(string url)
        {
            try
            {
                var host = new Uri(url).Host.ToLower();
                if (host.Contains("booth.pm")) return "BOOTH";
                if (host.Contains("ymm4-info.net")) return Translate.Ymm4InfoSite;
                if (host.Contains("twitter.com") || host.Contains("x.com")) return "X (Twitter)";
                if (host.Contains("youtube.com") || host.Contains("youtu.be")) return "YouTube";
                if (host.Contains("nicovideo.jp")) return Translate.Niconico;
                if (host.Contains("github.com")) return "GitHub";
                return host;
            }
            catch { return Translate.DistributionSite; }
        }
    }
}
