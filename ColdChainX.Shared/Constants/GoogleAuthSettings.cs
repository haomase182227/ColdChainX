namespace ColdChainX.Shared.Constants
{
    public class GoogleAuthSettings
    {
        public const string SectionName = "Authentication:Google";

        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
        public string RedirectUri { get; set; } = string.Empty;
        public string FrontendCallbackUrl { get; set; } = string.Empty;
        public bool BackendTestCallbackEnabled { get; set; }
    }
}
