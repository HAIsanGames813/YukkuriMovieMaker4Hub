namespace YukkuriMovieMaker4Hub
{
    public static class PluginTypeHelper
    {
        public static string GetDisplayName(string internalName) => internalName switch
        {
            "映像エフェクト" => Translate.VideoEffects,
            "音声エフェクト" => Translate.AudioEffects,
            "音声合成" => Translate.SpeechSynthesis,
            "動画出力" => Translate.VideoOutput,
            "動画読み込み" => Translate.LoadVideo,
            "音声読み込み" => Translate.LoadAudio,
            "画像読み込み" => Translate.LoadImage,
            "場面切り替え" => Translate.SceneTransition,
            "図形" => Translate.Shapes,
            "立ち絵" => Translate.Character,
            "ツール" => Translate.Tools,
            "テキスト補完" => Translate.TextCompletion,
            "模様" => Translate.Pattern,
            "文字起こし" => Translate.Transcription,
            "その他" => Translate.Others,
            "配布終了" => Translate.EndDistribution,
            _ => internalName
        };
    }
}
