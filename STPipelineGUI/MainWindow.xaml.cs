using System.Windows;
using STPipelineGUI.Models;
using STPipelineGUI.Services;

namespace STPipelineGUI;

public partial class MainWindow : Window
{
    private readonly PathService _paths = new();
    private readonly ManifestService _manifestService = new();
    private readonly UserConfigService _userConfigService = new();
    private readonly OutputFolderService _outputFolders;
    private readonly VdfGenerator _vdfGenerator;
    private readonly SteamCmdRunner _steamCmdRunner;
    private ManifestFile _manifest = new();
    private UserConfig _userConfig = new();
    private bool _isBindingProject;

    public MainWindow()
    {
        InitializeComponent();
        _outputFolders = new OutputFolderService(_paths);
        _vdfGenerator = new VdfGenerator(_paths);
        _steamCmdRunner = new SteamCmdRunner(_paths);
        Loaded += MainWindow_Loaded;
        RootPathText.Text = _paths.RootPath;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await ReloadAsync();
    }

    private async void ReloadButton_Click(object sender, RoutedEventArgs e)
    {
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        try
        {
            _manifest = await _manifestService.LoadAsync(_paths.ManifestPath);
            _userConfig = await _userConfigService.LoadAsync(_paths.UserConfigPath);
            ProjectsList.ItemsSource = _manifest.Projects;

            AppendLog($"Manifest cargado: {_paths.ManifestPath}");
            AppendLog(_manifest.Projects.Count == 1
                ? "1 proyecto disponible."
                : $"{_manifest.Projects.Count} proyectos disponibles.");

            if (!System.IO.File.Exists(_paths.UserConfigPath))
            {
                AppendLog("config/user.json no existe. Se usaran valores por defecto sin credenciales.");
            }
            else
            {
                AppendLog("config/user.json cargado. No se leen ni guardan passwords.");
            }

            ProjectsList.SelectedIndex = _manifest.Projects.Count > 0 ? 0 : -1;
            BindProject(SelectedProject);
        }
        catch (Exception ex)
        {
            AppendLog($"Error cargando configuracion: {ex.Message}");
        }
    }

    private SteamProject? SelectedProject => ProjectsList.SelectedItem as SteamProject;

    private void ProjectsList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        BindProject(SelectedProject);
    }

    private void BindProject(SteamProject? project)
    {
        _isBindingProject = true;

        if (project is null)
        {
            ProjectNameText.Text = "Sin proyectos";
            ProjectDescriptionText.Text = "Agrega proyectos a json/manifest.json.";
            AppIdText.Text = "-";
            BranchText.Text = "-";
            ContentRootText.Text = "-";
            BuildOutputText.Text = "-";
            WindowsCheckBox.IsChecked = false;
            LinuxCheckBox.IsChecked = false;
            MacosCheckBox.IsChecked = false;
            DepotsGrid.ItemsSource = null;
            _isBindingProject = false;
            return;
        }

        ProjectNameText.Text = project.Name;
        ProjectDescriptionText.Text = project.Description;
        AppIdText.Text = project.AppId;
        BranchText.Text = string.IsNullOrWhiteSpace(project.Branch) ? _userConfig.DefaultBranch : project.Branch;
        ContentRootText.Text = project.ContentRoot;
        BuildOutputText.Text = project.BuildOutput;
        WindowsCheckBox.IsChecked = project.Platforms.Windows;
        LinuxCheckBox.IsChecked = project.Platforms.Linux;
        MacosCheckBox.IsChecked = project.Platforms.Macos;
        DepotsGrid.ItemsSource = project.Depots;

        _isBindingProject = false;
    }

    private void PlatformCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isBindingProject || SelectedProject is not { } project)
        {
            return;
        }

        project.Platforms.Windows = WindowsCheckBox.IsChecked == true;
        project.Platforms.Linux = LinuxCheckBox.IsChecked == true;
        project.Platforms.Macos = MacosCheckBox.IsChecked == true;
    }

    private void CreateFoldersButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProject is not { } project)
        {
            AppendLog("Selecciona un proyecto antes de crear carpetas.");
            return;
        }

        var folders = _outputFolders.EnsureSelectedPlatformFolders(project);
        if (folders.Count == 0)
        {
            AppendLog("No hay plataformas seleccionadas.");
            return;
        }

        AppendLog("Carpetas listas:");
        foreach (var folder in folders)
        {
            AppendLog($"  {folder}");
        }
    }

    private void GenerateVdfButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProject is not { } project)
        {
            AppendLog("Selecciona un proyecto antes de generar VDF.");
            return;
        }

        var files = _vdfGenerator.Generate(project);
        AppendLog("VDF generado:");
        foreach (var file in files)
        {
            AppendLog($"  {file}");
        }
    }

    private async void RunSteamCmdButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProject is not { } project)
        {
            AppendLog("Selecciona un proyecto antes de preparar SteamCMD.");
            return;
        }

        var files = _vdfGenerator.Generate(project);
        var appBuildFile = files.FirstOrDefault(file => System.IO.Path.GetFileName(file).StartsWith("app_build_", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(appBuildFile))
        {
            AppendLog("No se pudo generar el app_build VDF.");
            return;
        }

        try
        {
            await _steamCmdRunner.RunAppBuildAsync(_userConfig, appBuildFile, AppendLog);
        }
        catch (Exception ex)
        {
            AppendLog($"SteamCMD no pudo ejecutarse: {ex.Message}");
        }
    }

    private void AppendLog(string message)
    {
        Dispatcher.Invoke(() =>
        {
            LogsTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            LogsTextBox.ScrollToEnd();
        });
    }
}
