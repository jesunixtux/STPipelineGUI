namespace STPipelineGUI.Models;

public sealed class UserConfig
{
    public string SteamCmdPath { get; set; } = "steamcmd/SteamCMD/steamcmd.exe";
    public string Username { get; set; } = string.Empty;
    public string DefaultBranch { get; set; } = "default";
}
