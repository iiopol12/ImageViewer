using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ImageViewer.Models
{
    /// <summary>
    /// 文件夹导航项 - 用于 Ctrl+Tab 快速切换
    /// </summary>
    public partial class FolderItem : ObservableObject
    {
        /// <summary>
        /// 文件夹完整路径
        /// </summary>
        [ObservableProperty]
        private string _path = string.Empty;

        /// <summary>
        /// 显示名称
        /// </summary>
        [ObservableProperty]
        private string _displayName = string.Empty;

        /// <summary>
        /// 文件夹图标
        /// </summary>
        [ObservableProperty]
        private ImageSource? _icon;

        /// <summary>
        /// 是否为"返回上级"项
        /// </summary>
        [ObservableProperty]
        private bool _isParentFolder;

        /// <summary>
        /// 是否被选中（高亮）
        /// </summary>
        [ObservableProperty]
        private bool _isSelected;

        /// <summary>
        /// 是否正在加载图标
        /// </summary>
        [ObservableProperty]
        private bool _isLoadingIcon = true;

        /// <summary>
        /// 创建"返回上级"文件夹项
        /// </summary>
        public static FolderItem CreateParentFolder(string parentPath)
        {
            return new FolderItem
            {
                Path = parentPath,
                DisplayName = "↑ 返回上级",
                IsParentFolder = true,
                IsLoadingIcon = false
            };
        }

        /// <summary>
        /// 创建普通文件夹项
        /// </summary>
        public static FolderItem CreateFolder(string path, string displayName)
        {
            return new FolderItem
            {
                Path = path,
                DisplayName = displayName,
                IsParentFolder = false,
                IsLoadingIcon = true
            };
        }
    }
}