namespace STPipelineGUI.Models;

public sealed class UserConfig
{
    public string SteamCmdPath { get; set; } = "steamcmd/SteamCMD/steamcmd.exe";
    public string Username { get; set; } = string.Empty;
    public string DefaultBranch { get; set; } = "default";
    public string LastProjectAppId { get; set; } = string.Empty;
    public bool AutoScrollLogs { get; set; } = true;
    public SteamworksConfig Steamworks { get; set; } = new();
}
