using System.Collections.ObjectModel;
using AndroidDevLink.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AndroidDevLink.Services;

public sealed class AppLogService : ObservableObject, IAppLogService
{
    private static readonly IReadOnlyList<int> SupportedLimits = [50, 100, 200, 300, 500];
    private readonly ObservableCollection<LogEntry> _entries = [];
    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;
    private int _maxEntries = 100;

    public AppLogService() => Entries = new(_entries);
    public ReadOnlyObservableCollection<LogEntry> Entries { get; }
    public IReadOnlyList<int> MaxEntriesOptions => SupportedLimits;

    public int MaxEntries
    {
        get => _maxEntries;
        set
        {
            if (!SupportedLimits.Contains(value)) return;
            if (SetProperty(ref _maxEntries, value)) TrimEntries();
        }
    }

    public void Add(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        if (_uiContext is { } context && SynchronizationContext.Current != context)
        {
            context.Post(_ => AddCore(message), null);
            return;
        }
        AddCore(message);
    }

    public void Clear()
    {
        if (_uiContext is { } context && SynchronizationContext.Current != context)
        {
            context.Post(_ => Clear(), null);
            return;
        }
        _entries.Clear();
    }

    private void AddCore(string message)
    {
        _entries.Add(new LogEntry(DateTime.Now, message));
        TrimEntries();
    }

    private void TrimEntries()
    {
        while (_entries.Count > _maxEntries) _entries.RemoveAt(0);
    }
}
