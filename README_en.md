# Android Dev Link

English | [简体中文](README.md)

A Windows desktop tool built with C# and WPF for managing Android devices through ADB. Browse device files, manage apps, queue uploads, request media scans, and launch scrcpy screen mirroring from one interface.

> The application UI is currently primarily in Simplified Chinese. These links switch the documentation language.

## Features

- **Device management:** list connected devices, inspect connection status and model information, and copy device serial numbers.
- **App management:** browse and filter system and third-party packages, install APKs, request app uninstallation, launch launcher activities, copy APK paths, and extract APK files.
- **File browser:** navigate Android directories, filter entries by name, upload files, download files or folders, create folders, rename entries, and delete entries with confirmation.
- **Upload queue:** both upload entry points share one sequential queue. Add more files during a transfer, select multiple files in the file browser, inspect task status, cancel individual tasks, and clear waiting items or finished records.
- **Media scanning:** request a scan for a selected image, audio, or video file, or for media files in the currently loaded directory. Media uploaded through the file browser is automatically submitted for scanning.
- **Device controls:** wake or sleep the screen, toggle auto-rotation, rotate the screen by 90°, and launch scrcpy mirroring.

## Requirements

- Windows with support for .NET 10 desktop applications.
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) to build and run from source.
- [Android SDK Platform-Tools](https://developer.android.com/tools/releases/platform-tools), including ADB.
- An Android device with **Developer options → USB debugging** enabled. Accept the USB debugging authorization prompt on the device.
- Optional: [scrcpy](https://github.com/Genymobile/scrcpy) for screen mirroring and control.

ADB and scrcpy must be installed separately.

## Configure external tools

For a simple setup, add the Platform-Tools and scrcpy directories to your Windows `PATH`, then restart the application.

The application searches for ADB in this order:

1. `platform-tools/adb.exe` beneath the application executable directory.
2. `platform-tools/adb.exe` beneath `ANDROID_SDK_ROOT`.
3. `platform-tools/adb.exe` beneath `ANDROID_HOME`.
4. `%LOCALAPPDATA%\Android\Sdk\platform-tools\adb.exe`.
5. `adb.exe` through executable lookup, including `PATH`.

For scrcpy, it first checks for `scrcpy.exe` beside the application executable, then uses executable lookup. If placing scrcpy beside the app, keep its required distribution files together.

Check your device connection:

```powershell
adb devices -l
```

The device should appear with the state `device`. For `unauthorized`, unlock the device and accept the debugging prompt.

## Build and run

In the directory containing `AndroidDevLink.csproj`, run:

```powershell
dotnet restore AndroidDevLink.csproj
dotnet build AndroidDevLink.csproj -c Debug
dotnet run --project AndroidDevLink.csproj
```

The default Debug executable is `bin/Debug/net10.0-windows/AndroidDevLink.exe`. You can also open the project in Visual Studio with .NET 10 support and the **.NET desktop development** workload.

## Usage

1. Connect a device, click **刷新设备** (Refresh devices), and select an online device.
2. Use the Windows path field and **选择文件** (Choose file), or drag a local file onto the window. Click **推送到 Android** (Push to Android) to queue a transfer to the entered device path.
3. Alternatively, open **文件浏览** (File browser), navigate to a directory, and click **上传文件** (Upload files). Multiple selected files are queued in selection order.
4. Open **上传队列** (Upload queue) to view the active task and pending or finished tasks.
5. To request media indexing, right-click a supported media file and choose **扫描到媒体库**, or click **扫描当前目录媒体**.

## Behavior and limitations

- Each queued upload retains the device and destination selected when it was added. Changing the current device or directory does not redirect existing tasks.
- The queue shows task states and an activity indicator, not percentage progress. It is kept in memory; closing the app cancels active and waiting uploads.
- Cancelling a transfer may leave an incomplete file on the device.
- Directory media scanning uses all media entries in the currently loaded directory, regardless of the name filter. It does not recurse into subdirectories; refresh the directory first if its contents have changed.
- A successful scan request does not confirm that the file has appeared in MediaStore or a player. Android version, permissions, format support, `.nomedia` files, and player caching can affect visibility.
- File access and system-app uninstallation depend on the device's ADB permissions. File deletion is permanent.

## Source structure

```text
AndroidDevLink.csproj
App.xaml                    Application startup
MainWindow.xaml             Main window and upload queue UI
Models/                     Device and file data models
Services/                   ADB, scrcpy, process execution, and output parsing
ViewModels/                 UI state, commands, and upload queue coordination
Views/                      File browser and dialogs
```

Built with **.NET 10**, **WPF**, and **CommunityToolkit.Mvvm 8.4.2**.
