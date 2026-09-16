namespace FractalExplorerWPF.Models;

public sealed record CloudSave(Guid Id, string Name, long Revision, DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt, string? JsonData = null);

// Deliberately not a record: generated ToString must never print credentials.
internal sealed class CloudTokens
{
    public string TokenType { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public long ExpiresIn { get; set; }
    public string RefreshToken { get; set; } = "";
}

internal sealed class CloudCredential
{
    public string Email { get; set; } = "";
    public string RefreshToken { get; set; } = "";
}

public sealed record LocalCloudSave(string FilePath, string Category, string Name, string JsonData, string Hash);
public sealed record CloudLink(Guid Id, string LocalPath, long Revision, string Hash);
public enum CloudConflictChoice { Cancel, KeepBoth, UseCloud, UseLocal }
