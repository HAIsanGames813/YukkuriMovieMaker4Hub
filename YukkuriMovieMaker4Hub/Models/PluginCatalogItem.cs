using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace YukkuriMovieMaker4Hub
{
    public class GitHubReleaseDetail
    {
        public string TagName { get; set; } = string.Empty;
        public string BrowserDownloadUrl { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public DateTime PublishedAt { get; set; }
        public bool Prerelease { get; set; }
    }

    public class PluginCatalogItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        public string Name { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Type { get; set; } = "";
        public bool IsEnabled { get; set; } = true;
        public string? Url { get; set; }
        public List<string> Links { get; set; } = new List<string>();
        public string Owner { get; set; } = string.Empty;
        public string Repo { get; set; } = string.Empty;
        private PluginLocalStatus _localStatus = PluginLocalStatus.NotInstalled;
        public PluginLocalStatus LocalStatus { get => _localStatus; set { _localStatus = value; OnPropertyChanged(nameof(LocalStatus)); OnPropertyChanged(nameof(IsInstalled)); } }
        private string _localVersion = "";
        public string LocalVersion { get => _localVersion; set { _localVersion = value; OnPropertyChanged(nameof(LocalVersion)); } }
        private ObservableCollection<GitHubReleaseDetail> _releases = new ObservableCollection<GitHubReleaseDetail>();
        public ObservableCollection<GitHubReleaseDetail> Releases { get => _releases; set { _releases = value; OnPropertyChanged(nameof(Releases)); } }
        private GitHubReleaseDetail? _selectedVersion;
        public GitHubReleaseDetail? SelectedVersion { get => _selectedVersion; set { _selectedVersion = value; OnPropertyChanged(nameof(SelectedVersion)); } }
        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); } }
        private bool _hasUpdate;
        public bool HasUpdate { get => _hasUpdate; set { _hasUpdate = value; OnPropertyChanged(nameof(HasUpdate)); } }
        private bool _isNew;
        public bool IsNew { get => _isNew; set { _isNew = value; OnPropertyChanged(nameof(IsNew)); } }
        private bool _isUnlinked;
        public bool IsUnlinked { get => _isUnlinked; set { _isUnlinked = value; OnPropertyChanged(nameof(IsUnlinked)); } }
        private bool _isLocalEnabled = true;
        public bool IsLocalEnabled { get => _isLocalEnabled; set { _isLocalEnabled = value; OnPropertyChanged(nameof(IsLocalEnabled)); } }
        
        public string Price { get; set; } = string.Empty;
        public List<string> Tags { get; set; } = new List<string>();
        public string License { get; set; } = string.Empty;
        public DateTime UpdatedAt { get; set; }
        public DateTime PublishedAt { get; set; }
        public bool IsExternalGh { get; set; } = false;
        public bool IsExternalBooth { get; set; } = false;

        public bool HasTags => Tags != null && Tags.Count > 0;

        public bool IsDirectDownloadSupported =>
            (!string.IsNullOrEmpty(Url) && Url.Contains("github.com"))
            || (Links != null && Links.Any(l => l != null && l.Contains("github.com")))
            || (!string.IsNullOrEmpty(Owner) && !string.IsNullOrEmpty(Repo));
        public bool IsInstalled => LocalStatus != PluginLocalStatus.NotInstalled;

        private bool _hasNoRelease;
        public bool HasNoRelease
        {
            get => _hasNoRelease;
            set { _hasNoRelease = value; OnPropertyChanged(nameof(HasNoRelease)); OnPropertyChanged(nameof(IsAssetDownloadable)); OnPropertyChanged(nameof(LatestVersionName)); }
        }

        private bool _releaseLoaded;
        public bool ReleaseLoaded
        {
            get => _releaseLoaded;
            set { _releaseLoaded = value; OnPropertyChanged(nameof(ReleaseLoaded)); OnPropertyChanged(nameof(IsAssetDownloadable)); }
        }

        public bool IsAssetDownloadable
        {
            get
            {
                if (!IsDirectDownloadSupported) return false;
                if (!ReleaseLoaded) return true;
                if (HasNoRelease) return false;
                if (Releases == null || Releases.Count == 0) return false;
                return Releases.Any(r =>
                {
                    var fn = r.FileName.ToLower();
                    return fn.EndsWith(".ymme") || fn.EndsWith(".zip") || fn.EndsWith(".dll");
                });
            }
        }
        public DateTime FirstPublishedAt => PublishedAt != default ? PublishedAt : (Releases != null && Releases.Count > 0 ? Releases.Min(r => r.PublishedAt) : DateTime.MinValue);
        public DateTime LatestPublishedAt => UpdatedAt != default ? UpdatedAt : (Releases != null && Releases.Count > 0 ? Releases.Max(r => r.PublishedAt) : (FirstPublishedAt != DateTime.MinValue ? FirstPublishedAt : DateTime.MinValue));
        public string LatestVersionName
        {
            get
            {
                if (!IsEnabled) return Translate.EndDistribution;
                if (!IsDirectDownloadSupported) return Translate.NoInfo;
                if (!ReleaseLoaded) return Translate.Acquiring;
                if (HasNoRelease) return Translate.EndDistribution;
                if (Releases == null || Releases.Count == 0) return Translate.Acquiring;
                if (!IsAssetDownloadable) return Translate.NoInfo;
                var best = Releases.FirstOrDefault(r =>
                {
                    var fn = r.FileName.ToLower();
                    return fn.EndsWith(".ymme") || fn.EndsWith(".zip") || fn.EndsWith(".dll");
                });
                return best?.TagName ?? Releases[0].TagName;
            }
        }
        public string DisplayVersion => string.IsNullOrEmpty(LatestVersionName) ? "v1.0.0" : LatestVersionName;

        public List<PluginLink> AllLinks
        {
            get
            {
                var list = new List<PluginLink>();
                if (!string.IsNullOrEmpty(Url)) try { list.Add(new PluginLink { Url = Url }); } catch { }
                foreach (var l in Links)
                {
                    if (string.IsNullOrWhiteSpace(l)) continue;
                    try { list.Add(new PluginLink { Url = l }); } catch { }
                }
                return list.GroupBy(x => x.Url).Select(g => g.First()).ToList();
            }
        }

        public string? BestSiteUrl
        {
            get
            {
                var nonGh = AllLinks.Where(l => { try { return !new Uri(l.Url).Host.Contains("github.com"); } catch { return false; } }).ToList();
                string? Find(IEnumerable<PluginLink> src, Func<string, bool> pred) =>
                    src.FirstOrDefault(l => { try { return pred(new Uri(l.Url).Host); } catch { return false; } })?.Url;
                return Find(nonGh, h => h.Contains("booth.pm"))
                    ?? Find(nonGh, h => h.Contains("ymm4-info.net"))
                    ?? Find(nonGh, h => h.Contains("twitter.com") || h.Contains("x.com"))
                    ?? Find(nonGh, h => h.Contains("youtube.com") || h.Contains("youtu.be"))
                    ?? Find(nonGh, h => h.Contains("nicovideo.jp"))
                    ?? nonGh.FirstOrDefault()?.Url;
            }
        }

        // GitHub Owner/Repo 自動補完
        public void EnsureGitHubOwnerRepo()
        {
            if (!string.IsNullOrEmpty(Owner) && !string.IsNullOrEmpty(Repo)) return;

            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(Url)) candidates.Add(Url);
            if (Links != null) candidates.AddRange(Links.Where(l => !string.IsNullOrWhiteSpace(l)));

            foreach (var candidate in candidates)
            {
                var m = System.Text.RegularExpressions.Regex.Match(candidate, @"github\.com/([^/]+)/([^/\?#]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    string o = m.Groups[1].Value.Trim();
                    string r = m.Groups[2].Value.Trim().TrimEnd('/');
                    if (r.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                        r = r.Substring(0, r.Length - 4);
                    if (!string.IsNullOrEmpty(o) && !string.IsNullOrEmpty(r) && !r.Equals("releases", StringComparison.OrdinalIgnoreCase))
                    {
                        Owner = o;
                        Repo = r;
                        return;
                    }
                }
            }
        }

        public string? GitHubUrl
        {
            get
            {
                EnsureGitHubOwnerRepo();
                if (!string.IsNullOrEmpty(Owner) && !string.IsNullOrEmpty(Repo))
                    return $"https://github.com/{Owner}/{Repo}";
                if (!string.IsNullOrEmpty(Url) && Url.Contains("github.com", StringComparison.OrdinalIgnoreCase))
                    return Url;
                return Links?.FirstOrDefault(l => l != null && l.Contains("github.com", StringComparison.OrdinalIgnoreCase));
            }
        }

        public string? GitHubReleasesUrl
        {
            get
            {
                EnsureGitHubOwnerRepo();
                if (!string.IsNullOrEmpty(Owner) && !string.IsNullOrEmpty(Repo))
                    return $"https://github.com/{Owner}/{Repo}/releases";
                var gh = GitHubUrl;
                if (!string.IsNullOrEmpty(gh))
                    return $"{gh.TrimEnd('/')}/releases";
                return null;
            }
        }

        public string? BoothUrl
        {
            get
            {
                if (!string.IsNullOrEmpty(Url) && Url.Contains("booth.pm", StringComparison.OrdinalIgnoreCase))
                    return Url;
                return Links?.FirstOrDefault(l => l != null && l.Contains("booth.pm", StringComparison.OrdinalIgnoreCase));
            }
        }

        public bool HasGitHub => !string.IsNullOrEmpty(GitHubUrl) || (!string.IsNullOrEmpty(Owner) && !string.IsNullOrEmpty(Repo));
        public bool HasBooth => !string.IsNullOrEmpty(BoothUrl);

        // 同一リポジトリ・同一プラグイン判定キー
        public string RepositoryKey
        {
            get
            {
                EnsureGitHubOwnerRepo();
                if (!string.IsNullOrEmpty(Owner) && !string.IsNullOrEmpty(Repo))
                    return $"gh:{Owner.ToLowerInvariant()}/{Repo.ToLowerInvariant()}";

                var ghUrl = GitHubUrl;
                if (!string.IsNullOrEmpty(ghUrl))
                {
                    var m = System.Text.RegularExpressions.Regex.Match(ghUrl, @"github\.com/([^/]+)/([^/\?#]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (m.Success)
                    {
                        var o = m.Groups[1].Value.ToLowerInvariant();
                        var r = m.Groups[2].Value.TrimEnd('/').ToLowerInvariant();
                        if (r.EndsWith(".git")) r = r.Substring(0, r.Length - 4);
                        if (!string.IsNullOrEmpty(o) && !string.IsNullOrEmpty(r))
                            return $"gh:{o}/{r}";
                    }
                }

                var boothUrl = BoothUrl;
                if (!string.IsNullOrEmpty(boothUrl))
                {
                    var bm = System.Text.RegularExpressions.Regex.Match(boothUrl, @"booth\.pm/([^/]+/)?items/(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (bm.Success)
                        return $"booth:{bm.Groups[2].Value}";
                }
                
                if (!string.IsNullOrEmpty(Url))
                    return $"url:{Url.Trim().ToLowerInvariant()}";
                
                return $"name:{Name.Trim().ToLowerInvariant()}";
            }
        }

        // --- ポータルUI用追加プロパティ ---
        public bool IsGitHub => HasGitHub;
        public bool IsBooth => HasBooth;
        
        public string SiteTag
        {
            get
            {
                if (IsGitHub) return "GitHub";
                if (IsBooth) return "BOOTH";
                
                string targetUrl = Url ?? AllLinks.FirstOrDefault()?.Url ?? string.Empty;
                if (string.IsNullOrWhiteSpace(targetUrl)) return "未設定";

                try
                {
                    var uri = new Uri(targetUrl);
                    var host = uri.Host.ToLowerInvariant();

                    if (host.Contains("github.com")) return "GitHub";
                    if (host.Contains("booth.pm")) return "BOOTH";
                    if (host.Contains("ymm4-info.net")) return "情報サイト";
                    if (host.Contains("drive.google.com")) return "Google Drive";
                    if (host.Contains("dropbox.com")) return "Dropbox";
                    if (host.Contains("bowlroll.net")) return "BowlRoll";
                    if (host.Contains("getuploader.com")) return "アップローダー";
                    if (host.Contains("youtube.com") || host.Contains("youtu.be")) return "YouTube";
                    if (host.Contains("nicovideo.jp")) return "ニコニコ動画";
                    if (host.Contains("ci-en.")) return "CI-en";
                    if (host.Contains("dlsite.com")) return "DLsite";
                    if (host.Contains("fanbox.cc")) return "FANBOX";
                    if (host.Contains("fantia.jp")) return "Fantia";
                    if (host.Contains("twitter.com") || host.Contains("x.com")) return "X (Twitter)";
                    if (host.Contains("onedrive.live.com")) return "OneDrive";
                    if (host.Contains("mega.nz")) return "MEGA";

                    // www. を除去してドメイン名を返す
                    if (host.StartsWith("www.")) host = host.Substring(4);
                    return host;
                }
                catch
                {
                    return "外部サイト";
                }
            }
        }
        public string VersionOrPrice
        {
            get
            {
                if (!string.IsNullOrEmpty(Price)) return Price;
                if (IsGitHub) return DisplayVersion;
                if (IsBooth) return "Booth";
                return "-";
            }
        }
        public string PrimaryActionLabel
        {
            get
            {
                if (IsGitHub) return "バージョン選択";
                if (IsBooth) return "Boothを開く";
                if (AllLinks.Count > 0)
                {
                    try
                    {
                        var host = new Uri(AllLinks[0].Url).Host;
                        return $"{host}を開く";
                    }
                    catch { }
                }
                return "サイトを開く";
            }
        }
        public string DisplayType => string.IsNullOrEmpty(Type) ? "その他" : Type;
        public string DisplayPublishedAt => LatestPublishedAt != DateTime.MinValue ? LatestPublishedAt.ToString("yyyy/MM/dd") : (FirstPublishedAt != DateTime.MinValue ? FirstPublishedAt.ToString("yyyy/MM/dd") : "2026/08/11");
        public bool IsYmlItem { get; set; } = true;
    }
}
