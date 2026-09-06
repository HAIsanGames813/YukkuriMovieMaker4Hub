using System;

namespace YukkuriMovieMaker4Hub
{
    public class ProjectFileItem
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public DateTime LastWriteTime { get; set; }
        public long FileSize { get; set; }
        public string Extension { get; set; } = string.Empty;
        public string DisplaySize => $"{FileSize / 1024.0 / 1024.0:F2} MB";
        public string DisplayDate => LastWriteTime.ToString("yyyy/MM/dd HH:mm");
    }
}
