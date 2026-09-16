# Android Dev Link

[English](README_en.md) | 简体中文

基于 C# / WPF 的 Windows 桌面 Android 设备管理工具，通过 ADB 提供设备管理、应用管理、文件浏览与队列上传、媒体扫描及 scrcpy 投屏控制。

> 当前应用界面以简体中文为主。顶部链接用于切换文档语言。

## 功能

- **设备管理**：查看已连接设备、连接状态与型号信息，复制设备序列号；设备插拔会自动触发刷新。
- **应用管理**：查看和筛选系统包、第三方包，安装 APK、请求卸载应用、启动 LAUNCHER、复制 APK 路径及提取 APK 文件。
- **文件浏览**：浏览 Android 目录、按名称筛选、上传文件、下载文件或文件夹、新建目录、重命名，以及带确认步骤的单个或多文件删除操作。
- **上传队列**：两个上传入口共用顺序队列；传输期间可继续添加文件，文件浏览上传支持多选；可查看任务状态、取消单项、清空等待项和清理已结束记录。
- **媒体扫描**：为选中的图片、音频、视频文件请求媒体索引，也可扫描当前已加载目录中的媒体文件；通过文件浏览上传的媒体会自动提交扫描请求。
- **日志记录**：在日志页查看底部状态事件，支持保留 50、100、200、300 或 500 条，并自动淘汰最早记录。
- **设备控制**：亮屏、息屏、切换自动旋转、旋转 90°，以及启动 scrcpy 投屏控制。

## 环境要求

- 支持 .NET 10 桌面应用的 Windows。
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)，用于从源码构建和运行。
- [Android SDK Platform-Tools](https://developer.android.com/tools/releases/platform-tools)，包含 ADB。
- Android 设备开启 **开发者选项 → USB 调试**，并在设备上允许电脑的 USB 调试授权。
- 可选：[scrcpy](https://github.com/Genymobile/scrcpy)，用于投屏与控制。

ADB 和 scrcpy 需要单独安装。

## 配置外部工具

推荐将 Platform-Tools 和 scrcpy 所在目录加入 Windows 的 `PATH`，然后重新启动应用。

程序按以下顺序查找 ADB：

1. 应用可执行文件目录下的 `platform-tools/adb.exe`。
2. `ANDROID_SDK_ROOT` 下的 `platform-tools/adb.exe`。
3. `ANDROID_HOME` 下的 `platform-tools/adb.exe`。
4. `%LOCALAPPDATA%\Android\Sdk\platform-tools\adb.exe`。
5. 通过可执行文件查找机制寻找 `adb.exe`，包括 `PATH`。

scrcpy 优先使用应用可执行文件旁的 `scrcpy.exe`，随后通过可执行文件查找机制寻找。如果将 scrcpy 放在应用目录，请同时保留其发行包中的必要依赖文件。

先确认设备连接：

```powershell
adb devices -l
```

设备状态应为 `device`。若显示 `unauthorized`，请解锁设备并接受 USB 调试授权。

## 构建与运行

在包含 `AndroidDevLink.csproj` 的目录中执行：

```powershell
dotnet restore AndroidDevLink.csproj
dotnet build AndroidDevLink.csproj -c Debug
dotnet run --project AndroidDevLink.csproj
```

默认 Debug 可执行文件为 `bin/Debug/net10.0-windows/AndroidDevLink.exe`。也可以使用支持 .NET 10、并安装了 **.NET 桌面开发** 工作负载的 Visual Studio 打开项目。

## 使用方法

1. 连接设备，点击 **刷新设备**，选择一台在线设备。
2. 在 Windows 路径旁点击 **选择文件**，或将本地文件拖入窗口；填写设备目标路径后，点击 **推送到 Android** 加入上传队列。
3. 也可以进入 **文件浏览**，打开目标目录后点击 **上传文件**；多选文件会按选择顺序排队。
4. 进入 **上传队列** 页面，查看当前上传、等待任务和已结束记录。
5. 需要媒体索引时，右键媒体文件选择 **扫描到媒体库**，或点击 **扫描当前目录媒体**。

## 行为与限制

- 每个上传任务固定保存入队时的设备和目标路径。切换当前设备或目录不会改变已有任务的目标。
- 队列显示任务状态和动态指示条，暂不提供百分比进度。队列保存在内存中，关闭程序会取消正在执行和等待中的上传。
- 日志记录保存在本次运行期间的内存中，关闭程序后不会保留。
- 取消传输后，设备端可能保留不完整的文件。
- 目录媒体扫描处理当前已加载目录中的全部媒体条目，不受名称筛选影响，也不递归子目录；目录内容发生变化后，请先刷新列表。
- 扫描请求成功不代表文件已经出现在系统媒体库或播放器中。Android 版本、权限、格式支持、`.nomedia` 文件和播放器缓存都可能影响显示。
- 文件访问和系统应用卸载取决于设备的 ADB 权限。文件删除为永久操作。

## 源码结构

```text
AndroidDevLink.csproj
App.xaml                    应用启动
MainWindow.xaml             主窗口与上传队列界面
Models/                     设备与文件数据模型
Services/                   ADB、scrcpy、进程执行及输出解析
ViewModels/                 界面状态、命令与上传队列调度
Views/                      文件浏览器与对话框
```

技术栈：**.NET 10**、**WPF**、**CommunityToolkit.Mvvm 8.4.2**。
