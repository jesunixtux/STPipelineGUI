namespace STPipelineGUI.Models;

public sealed class PlatformSelection
{
    public bool Windows { get; set; }
    public bool Linux { get; set; }
    public bool Macos { get; set; }

    public IEnumerable<string> SelectedPlatforms()
    {
        if (Windows)
        {
            yield return "windows";
        }

        if (Linux)
        {
            yield return "linux";
        }

        if (Macos)
        {
            yield return "macos";
        }
    }
}
