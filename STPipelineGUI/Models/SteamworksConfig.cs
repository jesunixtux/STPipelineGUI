namespace STPipelineGUI.Models;

public sealed class SteamworksConfig
{
    public bool Enabled { get; set; } = true;
    public string PublisherApiKey { get; set; } = string.Empty;
    public string PartnerApiBaseUrl { get; set; } = "https://partner.steam-api.com";
}
