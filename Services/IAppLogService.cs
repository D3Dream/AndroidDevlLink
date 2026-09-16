using System.Collections.ObjectModel;
using AndroidDevLink.Models;

namespace AndroidDevLink.Services;

public interface IAppLogService
{
    ReadOnlyObservableCollection<LogEntry> Entries { get; }
    IReadOnlyList<int> MaxEntriesOptions { get; }
    int MaxEntries { get; set; }
    void Add(string message);
    void Clear();
}
