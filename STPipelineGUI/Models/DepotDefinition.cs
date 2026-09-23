namespace STPipelineGUI.Models;

public sealed class DepotDefinition
{
    public string DepotId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string ContentRoot { get; set; } = string.Empty;
    public string InstallScript { get; set; } = string.Empty;
}
