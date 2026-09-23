namespace STPipelineGUI.Models;

public sealed class SteamProject
{
    public string AppId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Branch { get; set; } = "default";
    public string Description { get; set; } = string.Empty;
    public string ContentRoot { get; set; } = string.Empty;
    public string BuildOutput { get; set; } = "vdf/generated";
    public PlatformSelection Platforms { get; set; } = new();
    public List<DepotDefinition> Depots { get; set; } = [];

    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? AppId : Name;
}
