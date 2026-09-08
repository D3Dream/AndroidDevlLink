namespace AndroidDevLink.Models;

public sealed record AndroidDevice(string Serial, string State, string Description)
{
    public bool IsOnline => string.Equals(State, "device", StringComparison.OrdinalIgnoreCase);

    public string ModelName => Description
        .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .FirstOrDefault(deviceField => deviceField.StartsWith("model:", StringComparison.Ordinal))?
        ["model:".Length..] ?? string.Empty;

    public string DisplayName => string.IsNullOrWhiteSpace(Description)
        ? $"{Serial} ({State})"
        : $"{Serial} ({State}){Environment.NewLine}{Description}";
}
