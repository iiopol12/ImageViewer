# ImageViewer（漫游）

一款基于 WPF 的 Windows 图片查看器，支持图片、压缩包与 PDF 的浏览，面向漫画/图集场景优化。

## 主要功能
- 多格式浏览：常见图片、RAW、PDF、压缩包（支持密码）。
- 多视图模式：单图、双页、漫画长条、瀑布流缩略图。
- 便捷导航：缩放/适应/原始尺寸、拖拽平移、全屏、幻灯片（可随机）。
- 收藏与管理：图片书签、收藏文件夹、Ctrl+Tab 快速切换。
- 文件操作：复制到剪贴板、资源管理器定位、删除到回收站、LocalSend 分享。
- 设置丰富：主题与背景、滚轮行为、过滤器、子文件夹扫描、文件关联（图片格式）。

## 支持格式
- 图片：`.jpg` `.jpeg` `.png` `.gif` `.bmp` `.webp` `.tiff` `.tif` `.ico`
- RAW：`.dng` `.cr2` `.cr3` `.nef` `.arw` `.raf` `.rw2` `.orf` `.pef` `.srw`
- 扩展：`.avif` `.heic` `.heif` `.jxl` `.psd` `.tga` `.exr` `.dds` `.wp2`
- 压缩包：`.zip` `.cbz` `.rar` `.cbr`
- 文档：`.pdf`

## 快速上手
1. `Ctrl+O` 打开图片/压缩包/PDF，或 `Ctrl+F` 打开文件夹。
2. 支持拖拽文件/文件夹到窗口内直接加载。
3. 顶部工具栏可切换单图/双页/漫画模式与瀑布流视图。
4. 在设置中可开启子文件夹扫描、过滤器与文件关联。

## 快捷键（窗口激活时）
- 文件：`Ctrl+O` 打开文件，`Ctrl+F` 打开文件夹，`Ctrl+Tab` 切换收藏文件夹
- 导航：`←/→` 或 `A/D` 上一张/下一张，`Space` 下一张，`PgUp/PgDn` 上一张/下一张，`Home/End` 首张/末张
- 视图：`F11` 全屏，`Esc` 退出全屏/关闭，`+/-` 缩放，`Ctrl+0` 适应/原始尺寸切换
- 功能：`M` 切换浏览模式，`S` 幻灯片，`B` 收藏/取消收藏，`Ctrl+C` 复制到剪贴板，`Delete` 删除图片，`Ctrl+Shift+F` 启用/关闭过滤

## 构建与运行
需要 Windows 与支持 `net10.0-windows7.0` 的 .NET SDK。

```powershell
dotnet build .\ImageViewer.csproj
dotnet run --project .\ImageViewer.csproj
```

## 配置与数据位置
- 设置文件：`%APPDATA%\ImageViewer\settings.json`
- 文件关联：在“设置 → 关联设置”中为图片格式写入用户级注册表（PDF/压缩包仅支持应用内打开）。
- LocalSend：在设置中指定 LocalSend 可执行文件路径后可一键分享图片。
- 过滤器：仅对普通文件夹扫描生效，压缩包/PDF 不适用。

## PDF 说明
PDF 渲染依赖 `pdfium.dll`。如果启动后提示缺失或架构不匹配，请将正确架构的 `pdfium.dll` 放在可执行文件同目录。
