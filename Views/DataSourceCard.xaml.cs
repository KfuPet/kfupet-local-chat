using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using kfupet_local_chat.Models;
using kfupet_local_chat.Services;

namespace kfupet_local_chat.Views
{
    /// <summary>数据源卡片：手动选择归档记忆文件，并展示本试点长期记忆的条数。</summary>
    public partial class DataSourceCard : UserControl
    {
        private readonly MemoryStoreService _memories = new();

        public DataSourceCard()
        {
            InitializeComponent();
            Loaded += (_, _) => RefreshCounts();
        }

        public IReadOnlyList<ArchiveMemoryEntry> Entries { get; private set; } = Array.Empty<ArchiveMemoryEntry>();

        public string? ArchivePath { get; private set; }

        /// <summary>启动时只刷新计数；归档文件由用户手动选择。</summary>
        public void Initialize()
        {
            RefreshCounts();
        }

        /// <summary>选择任意归档 JSON 文件。</summary>
        public bool ChooseArchiveFile(out string message)
        {
            var docs = Path.Combine(AppPaths.ProjectRoot, "docs");
            var dialog = new OpenFileDialog
            {
                Title = "选择归档记忆文件",
                Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                InitialDirectory = Directory.Exists(docs) ? docs : AppPaths.ProjectRoot
            };

            if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            {
                message = string.Empty;
                return false;
            }

            if (!TryLoad(dialog.FileName, out var error))
            {
                message = error;
                MessageBox.Show(Window.GetWindow(this), error, "载入归档",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            RefreshCounts();
            message = $"归档记忆已载入：{Path.GetFileName(dialog.FileName)}（{Entries.Count} 条）";
            return true;
        }

        /// <summary>刷新归档与本试点长期记忆的条数。</summary>
        public void RefreshCounts()
        {
            ArchiveFileText.Text = ArchivePath == null ? "尚未选择" : Path.GetFileName(ArchivePath);
            ArchiveFileText.ToolTip = ArchivePath;
            ArchiveCountText.Text = ArchivePath == null ? "— 条" : $"{Entries.Count} 条";
            PilotLongCountText.Text = $"{_memories.Load().Count} 条";
        }

        /// <summary>全量清除后取消当前选择的归档文件。</summary>
        public void ResetSource()
        {
            Entries = Array.Empty<ArchiveMemoryEntry>();
            ArchivePath = null;
            RefreshCounts();
        }

        private bool TryLoad(string path, out string error)
        {
            try
            {
                Entries = ArchiveMemoryService.Load(path);
                ArchivePath = path;
                error = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                error = $"归档文件读取失败：{ex.Message}";
                return false;
            }
        }
    }
}
