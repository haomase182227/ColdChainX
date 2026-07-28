namespace ColdChainX.Infrastructure.Services.Firebase;

public class FirebaseOptions
{
    public const string SectionName = "Firebase";

    public string? ProjectId { get; set; }

    public string? ServiceAccountPath { get; set; }

    public string? ServiceAccountJson { get; set; }
}

public sealed class FirebaseConfigurationStatus
{
    public bool IsConfigured { get; init; }

    public string? Error { get; init; }

    public string? CredentialSource { get; init; }
}
