using System.Windows;
using STPipelineGUI.Models;

namespace STPipelineGUI;

public partial class AddProjectWindow : Window
{
    public AddProjectWindow()
    {
        InitializeComponent();
        WindowsCheckBox.IsChecked = true;
    }

    public SteamProject? Project { get; private set; }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var appId = AppIdTextBox.Text.Trim();
        var name = ProjectNameTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Escribe un nombre para el proyecto.", "Proyecto incompleto", MessageBoxButton.OK, MessageBoxImage.Information);
            ProjectNameTextBox.Focus();
            return;
        }

        if (!uint.TryParse(appId, out _))
        {
            MessageBox.Show("El AppID debe ser numérico.", "Proyecto incompleto", MessageBoxButton.OK, MessageBoxImage.Information);
            AppIdTextBox.Focus();
            return;
        }

        Project = new SteamProject
        {
            AppId = appId,
            Name = name,
            Branch = "default",
            Description = DescriptionTextBox.Text.Trim(),
            ContentRoot = $"output/{appId}",
            BuildOutput = $"steampipe/cache/{appId}",
            VdfOutput = "vdf/generated",
            Platforms = new PlatformSelection
            {
                Windows = WindowsCheckBox.IsChecked == true,
                Linux = LinuxCheckBox.IsChecked == true,
                Macos = MacosCheckBox.IsChecked == true
            },
            Depots =
            [
                CreateDepot(appId, "windows", "Windows", WindowsDepotIdTextBox.Text),
                CreateDepot(appId, "linux", "Linux", LinuxDepotIdTextBox.Text),
                CreateDepot(appId, "macos", "macOS", MacosDepotIdTextBox.Text)
            ]
        };

        DialogResult = true;
    }

    private static DepotDefinition CreateDepot(string appId, string platform, string name, string depotId)
    {
        return new DepotDefinition
        {
            DepotId = depotId.Trim(),
            Name = name,
            Platform = platform,
            ContentRoot = $"output/{appId}/{platform}"
        };
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
