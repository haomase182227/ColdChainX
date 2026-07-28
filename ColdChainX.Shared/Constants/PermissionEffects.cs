namespace ColdChainX.Shared.Constants;

public static class PermissionEffects
{
    public const string Allow = "ALLOW";
    public const string Deny = "DENY";

    public static bool IsValid(string? value)
        => string.Equals(value, Allow, StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, Deny, StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string value) => value.Trim().ToUpperInvariant();
}
