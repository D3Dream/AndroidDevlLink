using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Threading;
using AndroidDevLink.Models;
using AndroidDevLink.ViewModels;
using Microsoft.Win32;

namespace AndroidDevLink.Views;

public partial class DeviceFileBrowserView : UserControl
{
    private bool _isSyncingSelection;
    private bool _isContextMenuOpen;
    private AndroidFileEntry[] _contextSelection = [];

    public DeviceFileBrowserView()
    {
        InitializeComponent();
    }

    private DeviceFileBrowserViewModel? ViewModel => DataContext as DeviceFileBrowserViewModel;

    private async void FileDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        DataGridRow? row = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row?.DataContext is AndroidFileEntry { IsDirectory: true } directory)
        {
            await viewModel.NavigateToAsync(directory.FullPath);
            e.Handled = true;
            return;
        }

        if (viewModel.OpenSelectedEntryCommand.CanExecute(null))
        {
            await viewModel.OpenSelectedEntryCommand.ExecuteAsync(null);
        }
    }

    private async void QuickPath_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && sender is FrameworkElement { Tag: string path })
        {
            await viewModel.NavigateToAsync(path);
        }
    }

    private void FileDataGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        DataGridRow? row = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row is null)
        {
            FileDataGrid.SelectedItem = null;
            return;
        }

        if (row.DataContext is AndroidFileEntry entry && entry.IsDirectory)
        {
            e.Handled = true;
            return;
        }

        if (!row.IsSelected)
        {
            FileDataGrid.SelectedItems.Clear();
            row.IsSelected = true;
        }

        CaptureContextSelection();
    }

    private void FileDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingSelection || _isContextMenuOpen || ViewModel is not { } viewModel)
        {
            return;
        }

        _isSyncingSelection = true;
        try
        {
            foreach (AndroidFileEntry folder in FileDataGrid.SelectedItems
                         .OfType<AndroidFileEntry>()
                         .Where(entry => entry.IsDirectory)
                         .ToArray())
            {
                FileDataGrid.SelectedItems.Remove(folder);
            }

            AndroidFileEntry[] files = FileDataGrid.SelectedItems.OfType<AndroidFileEntry>().ToArray();
            _contextSelection = files;
            viewModel.SetSelectedEntries(files);
        }
        finally
        {
            _isSyncingSelection = false;
        }
    }

    private void FileDataGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (FileDataGrid.ContextMenu is not { } menu || ViewModel is not { } viewModel)
        {
            e.Handled = true;
            return;
        }

        CaptureContextSelection();

        AndroidFileEntry? entry = viewModel.SelectedEntry ?? _contextSelection.LastOrDefault();
        if (entry is null)
        {
            e.Handled = true;
            return;
        }

        _isContextMenuOpen = true;
        menu.Closed -= FileContextMenu_Closed;
        menu.Closed += FileContextMenu_Closed;

        foreach (object item in menu.Items)
        {
            if (item is not MenuItem menuItem)
            {
                continue;
            }

            menuItem.IsEnabled = menuItem.Header switch
            {
                "进入目录" => entry.IsDirectory,
                "扫描到媒体库" => entry.IsMediaFile,
                _ => true
            };
        }
    }

    private async void OpenDirectory_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && viewModel.OpenSelectedEntryCommand.CanExecute(null))
        {
            await viewModel.OpenSelectedEntryCommand.ExecuteAsync(null);
        }
    }

    private void CopyFullPath_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedEntry is not { } entry)
        {
            return;
        }

        Clipboard.SetText(entry.FullPath);
        ViewModel.StatusMessage = $"已复制路径：{entry.FullPath}";
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && viewModel.RefreshCommand.CanExecute(null))
        {
            await viewModel.RefreshCommand.ExecuteAsync(null);
        }
    }

    private async void ScanMediaFile_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && viewModel.ScanSelectedMediaCommand.CanExecute(null))
        {
            await viewModel.ScanSelectedMediaCommand.ExecuteAsync(null);
        }
    }

    private async void DownloadEntry_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedEntry is not { } entry)
        {
            return;
        }

        string? localTargetPath = entry.IsDirectory
            ? await SelectDownloadDirectoryAsync(entry)
            : await SelectDownloadFileAsync(entry);
        if (string.IsNullOrWhiteSpace(localTargetPath))
        {
            return;
        }

        if (ViewModel.DownloadSelectedEntryCommand.CanExecute(localTargetPath))
        {
            await ViewModel.DownloadSelectedEntryCommand.ExecuteAsync(localTargetPath);
        }
    }

    private Task<string?> SelectDownloadFileAsync(AndroidFileEntry entry) =>
        RunDialogOnStaThread(() =>
        {
            SaveFileDialog dialog = new()
            {
                Title = "下载 Android 文件",
                FileName = entry.Name,
                AddExtension = false,
                OverwritePrompt = true
            };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        });

    private Task<string?> SelectDownloadDirectoryAsync(AndroidFileEntry entry) =>
        RunDialogOnStaThread(() =>
        {
            OpenFolderDialog dialog = new()
            {
                Title = $"选择保存 {entry.Name} 的父目录",
                Multiselect = false
            };
            return dialog.ShowDialog() == true ? dialog.FolderName : null;
        });

    private static Task<string?> RunDialogOnStaThread(Func<string?> showDialog)
    {
        TaskCompletionSource<string?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread dialogThread = new(() =>
        {
            try
            {
                completion.SetResult(showDialog());
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        })
        {
            IsBackground = true
        };
        dialogThread.SetApartmentState(ApartmentState.STA);
        dialogThread.Start();
        return completion.Task;
    }

    private async void UploadFile_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        OpenFileDialog dialog = new()
        {
            Title = $"上传文件到 {viewModel.CurrentPath}",
            CheckFileExists = true,
            Multiselect = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        foreach (string fileName in dialog.FileNames)
        {
            if (viewModel.UploadFileCommand.CanExecute(fileName))
                await viewModel.UploadFileCommand.ExecuteAsync(fileName);
        }
    }

    private async void CreateDirectory_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        CreateDirectoryDialog dialog = new(
            title: $"在 {viewModel.CurrentPath} 中新建文件夹")
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (viewModel.CreateDirectoryCommand.CanExecute(dialog.DirectoryName))
        {
            await viewModel.CreateDirectoryCommand.ExecuteAsync(dialog.DirectoryName);
        }
    }

    private async void RenameEntry_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedEntry is not { } entry)
        {
            return;
        }

        CreateDirectoryDialog dialog = new(
            title: "重命名 Android 项目",
            prompt: "新名称",
            confirmButtonText: "重命名",
            initialName: entry.Name)
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (ViewModel.RenameSelectedEntryCommand.CanExecute(dialog.DirectoryName))
        {
            await ViewModel.RenameSelectedEntryCommand.ExecuteAsync(dialog.DirectoryName);
        }
    }

    private async void DeleteEntry_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        AndroidFileEntry[] entries = GetSelectedFilesForAction();
        if (entries.Length == 0)
        {
            return;
        }

        viewModel.SetSelectedEntries(entries);

        string itemType = entries.Length == 1 ? "文件" : $"{entries.Length} 个文件";
        string itemList = string.Join(Environment.NewLine, entries.Select(item => item.FullPath));
        MessageBoxResult firstConfirmation = MessageBox.Show(
            Window.GetWindow(this),
            $"即将永久删除 Android {itemType}：\n\n{itemList}\n\n此操作无法撤销。是否继续？",
            "确认删除",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (firstConfirmation != MessageBoxResult.Yes)
        {
            return;
        }

        MessageBoxResult secondConfirmation = MessageBox.Show(
            Window.GetWindow(this),
            $"请再次确认永久删除以上 {itemType}。",
            "最终确认删除",
            MessageBoxButton.YesNo,
            MessageBoxImage.Error,
            MessageBoxResult.No);
        if (secondConfirmation != MessageBoxResult.Yes)
        {
            return;
        }

        if (viewModel.DeleteSelectedEntriesCommand.CanExecute(null))
        {
            await viewModel.DeleteSelectedEntriesCommand.ExecuteAsync(null);
        }
    }

    private void FileContextMenu_Closed(object sender, RoutedEventArgs e)
    {
        _isContextMenuOpen = false;
        if (sender is ContextMenu menu)
        {
            menu.Closed -= FileContextMenu_Closed;
        }
    }

    private void CaptureContextSelection()
    {
        AndroidFileEntry[] files = FileDataGrid.SelectedItems
            .OfType<AndroidFileEntry>()
            .Where(entry => !entry.IsDirectory)
            .ToArray();
        if (files.Length == 0)
        {
            return;
        }

        if (_contextSelection.Length > files.Length &&
            files.All(file => _contextSelection.Contains(file)))
        {
            return;
        }

        _contextSelection = files;
    }

    private AndroidFileEntry[] GetSelectedFilesForAction()
    {
        AndroidFileEntry[] liveSelection = FileDataGrid.SelectedItems
            .OfType<AndroidFileEntry>()
            .Where(entry => !entry.IsDirectory)
            .ToArray();
        if (liveSelection.Length > 1)
        {
            return liveSelection;
        }

        if (_contextSelection.Length > 1 &&
            (liveSelection.Length == 0 || _contextSelection.Contains(liveSelection[0])))
        {
            return _contextSelection;
        }

        return liveSelection;
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
}
