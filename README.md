# ImageViewer（漫游）
一款基于 WPF 的 Windows 图片查看器，支持图片、压缩包与 PDF 的浏览，面向漫画/图集场景优化。  
A WPF-based Windows image viewer that supports images, archives, and PDFs, optimized for comics and galleries.

## 主要功能 / Features
- 多格式浏览：常见图片、RAW、PDF、压缩包（支持密码）。  
- 多视图模式：单图、双页、漫画长条、瀑布流缩略图。  
- 便捷导航：缩放/适应/原始尺寸、拖拽平移、全屏、幻灯片（可随机）。  
- 收藏与管理：图片书签、收藏文件夹、Ctrl+Tab 快速切换。 
- 文件与批量操作：复制到剪贴板、资源管理器定位、删除到回收站、LocalSend 分享、批量旋转/分享/删除/重命名/压缩/打印/收藏（瀑布流）。  
- 设置丰富：主题与背景、滚轮行为、过滤器、子文件夹扫描、文件关联（图片格式）。  

- Multi-format viewing: common images, RAW, PDF, archives (with password support).
- View modes: single, double-page, long-strip, and waterfall thumbnails.
- Navigation: zoom/fit/actual size, pan, fullscreen, slideshow (random supported).
- Library: bookmarks, favorite folders, quick switch with Ctrl+Tab.
- File & batch actions: copy to clipboard, locate in Explorer, recycle-bin delete, LocalSend share, batch rotate/share/delete/rename/zip/print/bookmark (waterfall view).
- Settings: themes/backgrounds, mouse wheel behavior, filters, subfolder scan, file associations (image formats).

## 支持格式 / Supported formats
- 图片 / Images: `.jpg` `.jpeg` `.png` `.gif` `.bmp` `.webp` `.tiff` `.tif` `.ico`
- RAW: `.dng` `.cr2` `.cr3` `.nef` `.arw` `.raf` `.rw2` `.orf` `.pef` `.srw`
- 扩展 / Extended: `.avif` `.heic` `.heif` `.jxl` `.psd` `.tga` `.exr` `.dds` `.wp2`
- 压缩包 / Archives: `.zip` `.cbz` `.rar` `.cbr`
- 文档 / Documents: `.pdf`

## 快速上手 / Quick start
1. `Ctrl+O` 打开图片/压缩包/PDF，或 `Ctrl+F` 打开文件夹。  
2. 支持拖拽文件/文件夹到窗口内直接加载。  
3. 顶部工具栏可切换单图/双页/漫画模式与瀑布流视图。  
4. 在设置中可开启子文件夹扫描、过滤器与文件关联。  

1. Press `Ctrl+O` to open image/archive/PDF, or `Ctrl+F` to open a folder.
2. Drag files/folders into the window to load.
3. Use the top toolbar to switch modes and waterfall view.
4. Enable subfolder scan, filters, and file associations in Settings.

## 快捷键（窗口激活时） / Shortcuts (window active)
- 文件 / Files: `Ctrl+O` 打开文件, `Ctrl+F` 打开文件夹, `Ctrl+Tab` 切换收藏文件夹  
  `Ctrl+O` open file, `Ctrl+F` open folder, `Ctrl+Tab` switch favorite folders
- 导航 / Navigation: `←/→` or `A/D` 上一张/下一张, `Space` 下一张, `PgUp/PgDn` 上一张/下一张, `Home/End` 首张/末张  
  `←/→` or `A/D` prev/next, `Space` next, `PgUp/PgDn` prev/next, `Home/End` first/last
- 视图 / View: `F11` 全屏, `Esc` 退出全屏/关闭, `+/-` 缩放, `Ctrl+0` 适应/原始尺寸切换  
  `F11` fullscreen, `Esc` exit fullscreen/close, `+/-` zoom, `Ctrl+0` fit/actual size toggle
- 功能 / Actions: `M` 切换浏览模式, `S` 幻灯片, `B` 收藏/取消收藏, `Ctrl+C` 复制到剪贴板, `Delete` 删除图片, `Ctrl+Shift+F` 启用/关闭过滤, `Ctrl+P` 打印  
  `M` mode switch, `S` slideshow, `B` bookmark toggle, `Ctrl+C` copy to clipboard, `Delete` delete image, `Ctrl+Shift+F` toggle filter, `Ctrl+P` print

## 配置与数据位置 / Configuration & data
- 设置文件 / Settings: `%APPDATA%\ImageViewer\settings.json`
- 文件关联 / File associations: 在“设置 → 关联设置”中为图片格式写入用户级注册表（PDF/压缩包仅支持应用内打开）。  
  Set image format associations in “Settings → Associations” (PDF/archives only open inside the app).
- LocalSend: 在设置中指定 LocalSend 可执行文件路径后可一键分享图片。  
  Set LocalSend executable path in Settings to enable one-click sharing.
- 过滤器 / Filters: 仅对普通文件夹扫描生效，压缩包/PDF 不适用。  
  Applies only to folder scans; archives/PDF are excluded.

## PDF 说明 / PDF notes
PDF 渲染依赖 `pdfium.dll`。如果启动后提示缺失或架构不匹配，请将正确架构的 `pdfium.dll` 放在可执行文件同目录。  
PDF rendering relies on `pdfium.dll`. If missing or architecture mismatched, place the correct `pdfium.dll` next to the executable.


## 多种主题
<img width="960" height="780" alt="PixPin_2026-01-02_20-29-37" src="https://github.com/user-attachments/assets/82714fdd-1e07-4cce-9e72-d725c7baeb17" />
<img width="800" height="600" alt="PixPin_2026-01-02_20-29-45" src="https://github.com/user-attachments/assets/3d5f827a-5e59-46cb-9d93-1345b08b9544" />
<img width="960" height="780" alt="PixPin_2025-12-22_19-57-39" src="https://github.com/user-attachments/assets/dd95349e-f9b1-4cb2-bf7b-52bd51fafc24" />
<img width="960" height="780" alt="PixPin_2025-12-22_19-57-31" src="https://github.com/user-attachments/assets/ab38fee6-1f41-4978-919a-c15a956e8cae" />
<img width="960" height="780" alt="PixPin_2025-12-22_19-57-25" src="https://github.com/user-attachments/assets/c888b1e5-8cfc-4f4c-9241-71bc225384db" />
<img width="960" height="780" alt="PixPin_2025-12-22_19-57-17" src="https://github.com/user-attachments/assets/ebd2b06f-53b3-47bf-8f6c-fbdd137c2599" />
