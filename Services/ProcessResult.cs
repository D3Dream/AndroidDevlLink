namespace AndroidDevLink.Services;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
