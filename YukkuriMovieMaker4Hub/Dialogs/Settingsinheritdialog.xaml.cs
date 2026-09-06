using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;

namespace YukkuriMovieMaker4Hub
{
    /// <summary>設定引き継ぎダイアログのアイテムモデル</summary>
    public class SettingsFileItem : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        void OnPropertyChanged(string n) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(n));

        public string FileName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsRecommended { get; set; }

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set { _isChecked = value; OnPropertyChanged(nameof(IsChecked)); }
        }
    }

    public partial class SettingsInheritDialog : Window
    {
        private readonly IReadOnlyList<InstanceInfo>? _instances;
        private string _settingsDir;   // readonly を外す（ShowInstanceSelector内で設定）

        private readonly List<SettingsFileItem> _items = new();

        public List<string> SelectedFiles { get; private set; } = new();
        public string SourceExePath { get; private set; } = string.Empty;

        // ──────────────────────────────────────────
        // JSONファイル → 表示名・説明・推奨 のマスタ（遅延初期化）
        // ──────────────────────────────────────────
        private static Dictionary<string, (string DisplayName, string Description, bool Recommended)>? _metaCache;
        private static Dictionary<string, (string DisplayName, string Description, bool Recommended)> GetMeta()
        {
            if (_metaCache != null) return _metaCache;
            _metaCache = new Dictionary<string, (string DisplayName, string Description, bool Recommended)>
            {
                ["YukkuriMovieMaker.Controls.BrushTypeComboBoxSettings.json"] = ("BrushTypeComboBoxSettings", "", false),
                ["YukkuriMovieMaker.Controls.ControlSettings.json"] = ("ControlSettings", Translate.MetaControlSettings, true),
                ["YukkuriMovieMaker.KanjiToYomi.Core.KanjiToYomiCoreSettings.json"] = ("KanjiToYomiCoreSettings", Translate.MetaKanjiToYomi, false),
                ["YukkuriMovieMaker.KanjiToYomi.UserDictionary.json"] = ("UserDictionary", Translate.MetaUserDictionary, true),
                ["YukkuriMovieMaker.Plugin.Community.Shape.Pen.PenSettings.json"] = ("PenSettings", Translate.MetaPen, false),
                ["YukkuriMovieMaker.Plugin.Community.TextCompletion.GoogleAI.GeminiTextCompletionSettings.json"] = ("GeminiTextCompletionSettings", Translate.MetaGemini, false),
                ["YukkuriMovieMaker.Plugin.Community.TextCompletion.Grok.GrokSettings.json"] = ("GrokSettings", Translate.MetaGrok, false),
                ["YukkuriMovieMaker.Plugin.Community.TextCompletion.Others.OthersSettings.json"] = ("OthersSettings", Translate.MetaAIOther, false),
                ["YukkuriMovieMaker.Plugin.Community.Tool.Browser.BrowserSettings.json"] = ("BrowserSettings", Translate.MetaBrowser, true),
                ["YukkuriMovieMaker.Plugin.Community.Tool.Explorer.ExplorerSettings.json"] = ("ExplorerSettings", Translate.MetaExplorer, true),
                ["YukkuriMovieMaker.Plugin.Community.Tool.Notepad.NotepadSettings.json"] = ("NotepadSettings", Translate.MetaNotepad, true),
                ["YukkuriMovieMaker.Plugin.Community.Tool.PluginPortal.PluginPortalSettings.json"] = ("PluginPortalSettings", Translate.MetaPluginPortal, false),
                ["YukkuriMovieMaker.Plugin.Community.Transcription.Whisper.WhisperTranscriptionSettings.json"] = ("WhisperTranscriptionSettings", "", false),
                ["YukkuriMovieMaker.Plugin.Community.Voice.AivisCloudAPI.AivisCloudAPISettings.json"] = ("AivisCloudAPISettings", Translate.MetaAivis, false),
                ["YukkuriMovieMaker.Plugin.Community.Voice.ElevenLabs.ElevenLabsSettings.json"] = ("ElevenLabsSettings", "", false),
                ["YukkuriMovieMaker.Plugin.Community.Voice.GeminiTTS.GeminiTTSSettings.json"] = ("GeminiTTSSettings", "", false),
                ["YukkuriMovieMaker.Plugin.Community.Voice.Kotodama.KotodamaSettings.json"] = ("KotodamaSettings", Translate.MetaKotodama, false),
                ["YukkuriMovieMaker.Plugin.Community.Voice.LivetoonTTS.LivetoonTTSSettings.json"] = ("LivetoonTTSSettings", "", false),
                ["YukkuriMovieMaker.Plugin.Community.Voice.VoiSonaTalk.VoiSonaTalkSettings.json"] = ("VoiSonaTalkSettings", Translate.MetaVoiSona, false),
                ["YukkuriMovieMaker.Plugin.FileSource.FFmpeg.FFmpegVideoFileWriterSettings.json"] = ("FFmpegVideoFileWriterSettings", Translate.MetaFFmpeg, true),
                ["YukkuriMovieMaker.Plugin.FileWriter.MediaFoundation.MFVideoFileWriterSettings.json"] = ("MFVideoFileWriterSettings", Translate.MetaMF, true),
                ["YukkuriMovieMaker.Plugin.FileWriter.PngWav.PngWavFileWriterSettings.json"] = ("PngWavFileWriterSettings", Translate.MetaPngWav, true),
                ["YukkuriMovieMaker.Plugin.PluginLoaderSettings.json"] = ("PluginLoaderSettings", Translate.MetaPluginLoader, false),
                ["YukkuriMovieMaker.Plugin.Tachie.AnimationTachie.AnimationTachieManagerSettings.json"] = ("AnimationTachieManagerSettings", "", false),
                ["YukkuriMovieMaker.Plugin.Tachie.Psd.PsdFileSettingsViewSettings.json"] = ("PsdFileSettingsViewSettings", "", false),
                ["YukkuriMovieMaker.Plugin.TextCompletion.OpenAI.OpenAIAPISettings.json"] = ("OpenAIAPISettings", Translate.MetaOpenAIToken, false),
                ["YukkuriMovieMaker.Plugin.TextCompletion.OpenAI.OpenAIChatTextCompletionPluginSettings.json"] = ("OpenAIChatTextCompletionPluginSettings", Translate.MetaChatGPT, false),
                ["YukkuriMovieMaker.Settings.AIVOICESettings.json"] = ("AIVOICESettings", Translate.MetaAIVoice, false),
                ["YukkuriMovieMaker.Settings.AmazonPollySettings.json"] = ("AmazonPollySettings", "", false),
                ["YukkuriMovieMaker.Settings.AqSettings.json"] = ("AqSettings", Translate.MetaAquesTalk, false),
                ["YukkuriMovieMaker.Settings.CeVIOSettings.json"] = ("CeVIOSettings", Translate.MetaCeVIO, false),
                ["YukkuriMovieMaker.Settings.CharacterEditorViewSettings.json"] = ("CharacterEditorViewSettings", Translate.MetaCharaListWidth, false),
                ["YukkuriMovieMaker.Settings.CharacterSettings.json"] = ("CharacterSettings", Translate.MetaCharacter, true),
                ["YukkuriMovieMaker.Settings.CoeFontSettings.json"] = ("CoeFontSettings", Translate.MetaCoeFont, false),
                ["YukkuriMovieMaker.Settings.COEIROINK2Settings.json"] = ("COEIROINK2Settings", Translate.MetaCoeiroink2, false),
                ["YukkuriMovieMaker.Settings.ColorSettings.json"] = ("ColorSettings", Translate.MetaColor, true),
                ["YukkuriMovieMaker.Settings.CommandSettings.json"] = ("CommandSettings", Translate.MetaShortcut, true),
                ["YukkuriMovieMaker.Settings.CommandSettingsViewProvider.json"] = ("CommandSettingsViewProvider", "", false),
                ["YukkuriMovieMaker.Settings.DeveloperModeSettings.json"] = ("DeveloperModeSettings", Translate.MetaDevMode, false),
                ["YukkuriMovieMaker.Settings.DeveloperModeSettingsProvider.json"] = ("DeveloperModeSettingsProvider", "", false),
                ["YukkuriMovieMaker.Settings.EffectEditorSettings.json"] = ("EffectEditorSettings", Translate.MetaEffectView, false),
                ["YukkuriMovieMaker.Settings.EffectSettings.json"] = ("EffectSettings", Translate.MetaEffectDefault, true),
                ["YukkuriMovieMaker.Settings.FileSettings.json"] = ("FileSettings", Translate.MetaFile, false),
                ["YukkuriMovieMaker.Settings.FontSettings.json"] = ("FontSettings", Translate.MetaFont, true),
                ["YukkuriMovieMaker.Settings.GoogleTTSSettings.json"] = ("GoogleTTSSettings", "", false),
                ["YukkuriMovieMaker.Settings.ItemSettings.json"] = ("ItemSettings", "", false),
                ["YukkuriMovieMaker.Settings.MicrosoftAzureTTSSettings.json"] = ("MicrosoftAzureTTSSettings", "", false),
                ["YukkuriMovieMaker.Settings.OpenAITTSSettings.json"] = ("OpenAITTSSettings", "", false),
                ["YukkuriMovieMaker.Settings.ResourceDirectorySettings.json"] = ("ResourceDirectorySettings", "", false),
                ["YukkuriMovieMaker.Settings.SAPI5VoiceSettings.json"] = ("SAPI5VoiceSettings", "", false),
                ["YukkuriMovieMaker.Settings.SearchSettings.json"] = ("SearchSettings", Translate.MetaSearch, false),
                ["YukkuriMovieMaker.Settings.SettingsViewSettings.json"] = ("SettingsViewSettings", "", false),
                ["YukkuriMovieMaker.Settings.ShapeSettings.json"] = ("ShapeSettings", "", false),
                ["YukkuriMovieMaker.Settings.TALQuSettings.json"] = ("TALQuSettings", "", false),
                ["YukkuriMovieMaker.Settings.TemplateEditorViewSettings.json"] = ("TemplateEditorViewSettings", "", false),
                ["YukkuriMovieMaker.Settings.TextCompletionServiceSettings.json"] = ("TextCompletionServiceSettings", "", false),
                ["YukkuriMovieMaker.Settings.TimelineResourceSettings.json"] = ("TimelineResourceSettings", "", false),
                ["YukkuriMovieMaker.Settings.TimelineSettings.json"] = ("TimelineSettings", Translate.MetaTimeline, true),
                ["YukkuriMovieMaker.Settings.TransitionSettings.json"] = ("TransitionSettings", "", false),
                ["YukkuriMovieMaker.Settings.VOICEPEAKSettings.json"] = ("VOICEPEAKSettings", Translate.MetaVoicepeak, false),
                ["YukkuriMovieMaker.Settings.VOICEVOXSettings.json"] = ("VOICEVOXSettings", Translate.MetaVoicevox, false),
                ["YukkuriMovieMaker.Settings.VolumeAnalyzerSettings.json"] = ("VolumeAnalyzerSettings", "", false),
                ["YukkuriMovieMaker.Settings.YMMSettings.json"] = ("YMMSettings", Translate.MetaYMMSettings, true),
                ["YukkuriMovieMaker.Settings.YomiteProgramSettings.json"] = ("YomiteProgramSettings", "", false),
                ["YukkuriMovieMaker.Transcription.TranscriptionSettings.json"] = ("TranscriptionSettings", "", false),
                ["YukkuriMovieMaker.VideoFileWriter.ResourceDirectorySettingsViewProvider.json"] = ("ResourceDirectorySettingsViewProvider", "", false),
                ["YukkuriMovieMaker.VideoFileWriter.VideoFileWriterSettings.json"] = ("VideoFileWriterSettings", "", false),
                ["YukkuriMovieMaker.VideoFileWriter.YMMSettingsViewProvider.json"] = ("YMMSettingsViewProvider", "", false),
                ["YukkuriMovieMaker.Voice.StyleBertVits2.StyleBertVits2Settings.json"] = ("StyleBertVits2Settings", "", false),
            };
            return _metaCache;
        }

        // ──────────────────────────────────────────
        // コンストラクタ
        // ──────────────────────────────────────────

        /// <summary>settingsDirを直接指定（後方互換）</summary>
        public SettingsInheritDialog(string settingsDir)
        {
            _settingsDir = settingsDir;
            _instances = null;
            InitializeComponent();
            ThemeHelper.Sync(this);
            LoadFiles();
        }

        /// <summary>インスタンスリストを渡して引き継ぎ元を選ばせる</summary>
        public SettingsInheritDialog(IReadOnlyList<InstanceInfo> instances)
        {
            _settingsDir = string.Empty;
            _instances = instances;
            InitializeComponent();
            ThemeHelper.Sync(this);
            // Loaded 後にインスタンス選択ダイアログを表示（Owner が確定してから）
            Loaded += (s, e) => ShowInstanceSelector();
        }

        private void ShowInstanceSelector()
        {
            if (_instances == null || _instances.Count == 0)
            {
                MessageBox.Show(Translate.NoOtherInstance);
                DialogResult = false;
                return;
            }
            var selector = new InstanceSelectorDialog(_instances) { Owner = this };
            if (selector.ShowDialog() != true || selector.SelectedInstance == null)
            {
                DialogResult = false;
                return;
            }
            string exePath = selector.SelectedInstance.ExePath;
            SourceExePath = exePath;
            _settingsDir = Path.Combine(Path.GetDirectoryName(exePath) ?? "", "user", "setting");
            LoadFiles();
        }

        private void LoadFiles()
        {
            _items.Clear();
            try
            {
                // 最新バージョンフォルダを探す
                string baseDir = _settingsDir;
                if (Directory.Exists(baseDir))
                {
                    var versionDirs = new DirectoryInfo(baseDir).GetDirectories()
                        .OrderByDescending(d => d.LastWriteTime).ToArray();
                    if (versionDirs.Length > 0)
                        baseDir = versionDirs[0].FullName;
                }

                if (!Directory.Exists(baseDir)) return;

                // フォルダ内の全JSONを表示（公式・サードパーティ問わず）
                var jsonFiles = Directory.GetFiles(baseDir, "*.json")
                    .Select(Path.GetFileName)
                    .Where(f => f != null)
                    .Cast<string>()
                    .OrderBy(f => f)
                    .ToList();

                foreach (var fn in jsonFiles)
                {
                    bool isKnown = GetMeta().TryGetValue(fn, out var meta);
                    string rawName = Path.GetFileNameWithoutExtension(fn);
                    string displayName = isKnown ? meta.DisplayName : rawName;
                    if (displayName.StartsWith("YukkuriMovieMaker."))
                        displayName = displayName.Substring("YukkuriMovieMaker.".Length);

                    _items.Add(new SettingsFileItem
                    {
                        FileName = fn,
                        DisplayName = displayName,
                        Description = isKnown ? meta.Description : "",
                        IsRecommended = isKnown && meta.Recommended,
                        IsChecked = isKnown && meta.Recommended,
                    });
                }
            }
            catch { }

            FileList.ItemsSource = _items;
        }

        private void UseInherit_Changed(object sender, RoutedEventArgs e)
        {
            bool use = UseInheritCheckBox.IsChecked == true;
            if (FileList != null) FileList.IsEnabled = use;
            if (CheckAllButton != null) CheckAllButton.IsEnabled = use;
            if (UncheckAllButton != null) UncheckAllButton.IsEnabled = use;
            if (OkButton != null) OkButton.IsEnabled = use;
        }

        private void CheckAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _items) item.IsChecked = true;
        }

        private void UncheckAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _items) item.IsChecked = false;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            SelectedFiles = _items.Where(i => i.IsChecked).Select(i => i.FileName).ToList();
            DialogResult = true;
        }

        private void Skip_Click(object sender, RoutedEventArgs e)
        {
            SelectedFiles = new List<string>();
            DialogResult = false;
        }
    }
}