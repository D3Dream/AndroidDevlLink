using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AndroidDevLink.Models;
using AndroidDevLink.Services;
using AndroidDevLink.ViewModels;

namespace AndroidDevLink;

public partial class MainWindow : Window
{
    public MainWindow()
        : this(new MainWindowViewModel(new AdbService(), new ScrcpyService()))
    {
    }

    public MainWindow(IAdbService adbService)
        : this(new MainWindowViewModel(adbService))
    {
    }

    public MainWindow(MainWindowViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = ViewModel;
        Closed += (_, _) => ViewModel.Dispose();
    }

    public MainWindowViewModel ViewModel { get; }

    private async void Window_Loaded(object sender, RoutedEventArgs e) =>
        await ViewModel.RefreshDevicesAsync();

    private void CopyPackageName_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedPackageName(sender, out string packageName))
        {
            return;
        }

        Clipboard.SetText(packageName);
        ViewModel.SetStatus($"已复制包名：{packageName}");
    }

    private async void CopyApkPath_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedPackageName(sender, out string packageName))
        {
            return;
        }

        IReadOnlyList<string> paths = await ViewModel.GetPackageApkPathsAsync(packageName);
        if (paths.Count == 0)
        {
            return;
        }

        Clipboard.SetText(string.Join(Environment.NewLine, paths));
        ViewModel.SetStatus(paths.Count == 1
            ? $"已复制 APK 路径：{paths[0]}"
            : $"已复制 {paths.Count} 个 APK 路径。");
    }

    private async void ExtractApkToDesktop_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedPackageName(sender, out string packageName))
        {
            return;
        }

        Microsoft.Win32.SaveFileDialog dialog = new()
        {
            Title = "提取 APK 到桌面",
            FileName = packageName,
            DefaultExt = ".apk",
            Filter = "APK 文件 (*.apk)|*.apk|所有文件 (*.*)|*.*",
            AddExtension = true,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await ViewModel.ExtractApkToFileAsync(packageName, dialog.FileName);
    }

    private void SelectWindowsPath_Click(object sender, RoutedEventArgs e)
    {
        Microsoft.Win32.OpenFileDialog dialog = new()
        {
            Title = "选择文件",
            Filter = "所有文件 (*.*)|*.*|APK 文件 (*.apk)|*.apk",
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = false,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        ViewModel.WindowsPath = dialog.FileName;
        ViewModel.SetStatus($"已选择文件：{dialog.FileName}");
    }

    // 右键点击列表项时先将其设为当前项，菜单操作始终作用于鼠标指向的数据。
    private void ListBox_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox listBox)
        {
            return;
        }

        DependencyObject? source = e.OriginalSource as DependencyObject;
        ListBoxItem? item = FindVisualParent<ListBoxItem>(source);
        if (item is null || !ReferenceEquals(ItemsControl.ItemsControlFromItemContainer(item), listBox))
        {
            listBox.SelectedItem = null;
            return;
        }

        item.IsSelected = true;
        item.Focus();
    }

    private void ListBox_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not ListBox listBox || listBox.SelectedItem is null)
        {
            e.Handled = true;
        }
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T parent)
            {
                return parent;
            }

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }

    // 在预览阶段处理拖放，确保拖到窗口内任意控件都能识别本地文件。
    private void Window_PreviewDragOver(object sender, DragEventArgs e)
    {
        bool isFile = GetDroppedFilePath(e.Data) is not null;
        e.Effects = isFile ? DragDropEffects.Copy : DragDropEffects.None;
        WindowsPathTextBox.Background = isFile
            ? new SolidColorBrush(Color.FromRgb(221, 247, 232))
            : Brushes.White;
        e.Handled = true;
    }

    private void Window_PreviewDragLeave(object sender, DragEventArgs e)
    {
        WindowsPathTextBox.Background = Brushes.White;
    }

    private void Window_PreviewDrop(object sender, DragEventArgs e)
    {
        string? filePath = GetDroppedFilePath(e.Data);
        WindowsPathTextBox.Background = Brushes.White;
        if (filePath is null)
        {
            ViewModel.SetStatus("请拖入一个存在的文件。");
            e.Handled = true;
            return;
        }

        ViewModel.WindowsPath = filePath;
        ViewModel.SetStatus($"已填入文件路径：{Path.GetFileName(filePath)}");
        e.Handled = true;
    }

    private bool TryGetSelectedPackageName(object sender, out string packageName)
    {
        packageName = string.Empty;
        string? listKind = (sender as FrameworkElement)?.Tag as string;
        ListBox? packageListBox = listKind switch
        {
            "System" => SystemPackageListBox,
            "ThirdParty" => ThirdPartyPackageListBox,
            _ => null
        };

        if (packageListBox?.SelectedItem is not string selectedPackage)
        {
            ViewModel.SetStatus("请先选择一个包名。");
            return false;
        }

        packageName = selectedPackage;
        return true;
    }

    // 路径输入框只保存一个路径，因此一次拖放只接受一个存在的普通文件。
    private static string? GetDroppedFilePath(IDataObject data)
    {
        if (!data.GetDataPresent(DataFormats.FileDrop) ||
            data.GetData(DataFormats.FileDrop) is not string[] files ||
            files.Length != 1)
        {
            return null;
        }

        string filePath = files[0];
        return File.Exists(filePath) ? filePath : null;
    }

    private void CopySelectedSerial_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedDevice is null)
        {
            ViewModel.SetStatus("请先选择一个设备。");
            return;
        }

        Clipboard.SetText(ViewModel.SelectedDevice.Serial);
        ViewModel.SetStatus($"已复制 SN：{ViewModel.SelectedDevice.Serial}");
    }

    private void CopySelectedModelName_Click(object sender, RoutedEventArgs e)
    {
        AndroidDevice? device = ViewModel.SelectedDevice;
        if (device is null)
        {
            ViewModel.SetStatus("请先选择一个设备。");
            return;
        }

        if (string.IsNullOrWhiteSpace(device.ModelName))
        {
            ViewModel.SetStatus($"设备 {device.Serial} 没有可复制的 Model 名。");
            return;
        }

        Clipboard.SetText(device.ModelName);
        ViewModel.SetStatus($"已复制 Model 名：{device.ModelName}");
    }

    private void CopyAllSerials_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Devices.Count == 0)
        {
            ViewModel.SetStatus("没有可复制的设备 SN。");
            return;
        }

        Clipboard.SetText(string.Join(
            Environment.NewLine,
            ViewModel.Devices.Select(device => device.Serial)));
        ViewModel.SetStatus($"已复制 {ViewModel.Devices.Count} 个设备 SN。");
    }
}
