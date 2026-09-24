using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
    private readonly SteamworksApiClient _steamworksApiClient = new();
    private readonly List<SteamProject> _visibleProjects = [];
    private ManifestFile _manifest = new();
    private UserConfig _userConfig = new();
    private bool _isBindingProject;
    private bool _isLoadingConfig;
    private bool _operationInProgress;
    private bool _steamCmdConsoleAtLineStart = true;
    private CancellationTokenSource? _branchLoadCts;
    private CancellationTokenSource? _saveCts;
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    public MainWindow()
    {
        InitializeComponent();
        _outputFolders = new OutputFolderService(_paths);
        _vdfGenerator = new VdfGenerator(_paths);
        _steamCmdRunner = new SteamCmdRunner(_paths);
        Loaded += MainWindow_Loaded;
        FooterClockText.Text = DateTime.Now.ToString("HH:mm");
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        try
        {
            _manifest = await _manifestService.LoadAsync(_paths.ManifestPath);
            _userConfig = await _userConfigService.LoadAsync(_paths.UserConfigPath);
            _userConfig.Steamworks ??= new SteamworksConfig();
            _userConfig.Steamworks.Enabled = !string.IsNullOrWhiteSpace(_userConfig.Steamworks.PublisherApiKey);
            _isLoadingConfig = true;
            try
            {
                AutoScrollCheckBox.IsChecked = _userConfig.AutoScrollLogs;
                SteamCmdPathTextBox.Text = _userConfig.SteamCmdPath;
                UsernameTextBox.Text = _userConfig.Username;
                SteamworksApiBaseUrlTextBox.Text = _userConfig.Steamworks.PartnerApiBaseUrl;
                SteamworksApiKeyPasswordBox.Password = _userConfig.Steamworks.PublisherApiKey;
            }
            finally
            {
                _isLoadingConfig = false;
            }

            ApplyProjectFilter();
            var remembered = _visibleProjects.FirstOrDefault(p => p.AppId == _userConfig.LastProjectAppId);
            ProjectsList.SelectedItem = remembered ?? _visibleProjects.FirstOrDefault();
            BindProject(SelectedProject);
            await LoadBranchesForProjectAsync(SelectedProject);

            UpdateSteamCmdStatus(File.Exists(_steamCmdRunner.ResolveSteamCmdPath(_userConfig)));
            UpdateSteamworksStatusFromConfig();
            AppendLog($"Manifest cargado: {_paths.ManifestPath}");
            AppendLog(File.Exists(_paths.UserConfigPath)
                ? "config/user.json cargado. No se leen ni guardan passwords."
                : "config/user.json no existe. Se usaran valores por defecto sin credenciales.");
        }
        catch (Exception ex)
        {
            AppendLog($"Error cargando configuracion: {ex.Message}");
        }
    }

    private SteamProject? SelectedProject => ProjectsList.SelectedItem as SteamProject;

    private void ApplyProjectFilter()
    {
        var query = ProjectSearchBox.Text?.Trim() ?? string.Empty;
        _visibleProjects.Clear();
        _visibleProjects.AddRange(_manifest.Projects.Where(project =>
            string.IsNullOrWhiteSpace(query) ||
            project.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            project.AppId.Contains(query, StringComparison.OrdinalIgnoreCase)));

        ProjectsList.ItemsSource = null;
        ProjectsList.ItemsSource = _visibleProjects;
    }

    private void ProjectSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var currentAppId = SelectedProject?.AppId;
        ApplyProjectFilter();
        ProjectsList.SelectedItem = _visibleProjects.FirstOrDefault(p => p.AppId == currentAppId) ?? _visibleProjects.FirstOrDefault();
    }

    private async void ProjectsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            var project = SelectedProject;
            BindProject(project);

            if (project is not null && !_isBindingProject)
            {
                _userConfig.LastProjectAppId = project.AppId;
                await SaveUserConfigAsync();
            }

            await LoadBranchesForProjectAsync(project);
        }
        catch (Exception ex)
        {
            AppendLog($"No se pudo cambiar de proyecto: {ex.Message}");
        }
    }

    private async void AddProjectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_operationInProgress)
        {
            return;
        }

        var dialog = new AddProjectWindow
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true || dialog.Project is not { } project)
        {
            return;
        }

        if (_manifest.Projects.Any(existing => existing.AppId.Equals(project.AppId, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("Ya existe un proyecto con ese AppID.", "Proyecto duplicado", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _manifest.Projects.Add(project);
        ProjectSearchBox.Clear();
        ApplyProjectFilter();
        ProjectsList.SelectedItem = project;
        await SaveAllAsync();
        AppendLog($"Proyecto agregado: {project.Name} ({project.AppId}).");
    }

    private async Task LoadBranchesForProjectAsync(SteamProject? project)
    {
        CancelBranchLoad();

        if (project is null)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _branchLoadCts = cts;

        try
        {
            SetBranchOptions(project, [NormalizeBranch(project.Branch), "default"]);

            if (!_userConfig.Steamworks.Enabled || string.IsNullOrWhiteSpace(_userConfig.Steamworks.PublisherApiKey))
            {
                BranchStatusText.Text = "Configura Steamworks para cargar las ramas reales.";
                return;
            }

            RefreshBranchesButton.IsEnabled = false;
            BranchStatusText.Text = "Cargando ramas desde Steamworks...";

            var result = await _steamworksApiClient.GetBranchesAsync(_userConfig.Steamworks, project.AppId, cts.Token);
            if (cts.IsCancellationRequested || !ReferenceEquals(SelectedProject, project))
            {
                return;
            }

            if (result.Success)
            {
                var branches = result.Branches
                    .Append(NormalizeBranch(project.Branch))
                    .Append("default");
                SetBranchOptions(project, branches);
                BranchStatusText.Text = result.Message;
            }
            else
            {
                BranchStatusText.Text = result.Message;
                AppendLog($"Steamworks ramas: {result.Message}");
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            BranchStatusText.Text = "No se pudieron cargar las ramas.";
            AppendLog($"Steamworks ramas: {ex.Message}");
        }
        finally
        {
            if (!cts.IsCancellationRequested && ReferenceEquals(SelectedProject, project))
            {
                RefreshBranchesButton.IsEnabled = true;
            }

            if (ReferenceEquals(_branchLoadCts, cts))
            {
                _branchLoadCts = null;
            }

            cts.Dispose();
        }
    }

    private void CancelBranchLoad()
    {
        var cts = _branchLoadCts;
        _branchLoadCts = null;

        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void SetBranchOptions(SteamProject project, IEnumerable<string> branches)
    {
        var options = branches
            .Where(branch => !string.IsNullOrWhiteSpace(branch))
            .Select(branch => branch.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        BranchComboBox.ItemsSource = options;
        BranchComboBox.Text = NormalizeBranch(project.Branch);
    }

    private void BindProject(SteamProject? project)
    {
        _isBindingProject = true;

        if (project is null)
        {
            ProjectNameText.Text = "Sin proyectos";
            ProjectDescriptionText.Text = "Agrega proyectos manualmente en json/manifest.json.";
            HeaderAppIdText.Text = "AppID: -";
            HeaderBranchText.Text = "Branch: -";
            AppIdTextBox.Text = string.Empty;
            BranchComboBox.Text = "default";
            BranchComboBox.ItemsSource = Array.Empty<string>();
            BranchStatusText.Text = "No hay proyecto seleccionado.";
            RefreshBranchesButton.IsEnabled = false;
            BuildCacheTextBox.Text = string.Empty;
            VdfOutputTextBox.Text = "vdf/generated";
            WindowsCheckBox.IsChecked = false;
            LinuxCheckBox.IsChecked = false;
            MacosCheckBox.IsChecked = false;
            WindowsDepotIdTextBox.Text = string.Empty;
            LinuxDepotIdTextBox.Text = string.Empty;
            MacosDepotIdTextBox.Text = string.Empty;
            WindowsPathTextBox.Text = string.Empty;
            LinuxPathTextBox.Text = string.Empty;
            MacosPathTextBox.Text = string.Empty;
            VdfPreviewTextBox.Text = string.Empty;
            PreflightText.Text = "No project selected.";
            _isBindingProject = false;
            return;
        }

        EnsureProjectDefaults(project);
        ProjectNameText.Text = project.Name;
        ProjectDescriptionText.Text = project.Description;
        HeaderAppIdText.Text = $"AppID: {project.AppId}";
        HeaderBranchText.Text = $"Branch: {NormalizeBranch(project.Branch)}";
        AppIdTextBox.Text = project.AppId;
        SetBranchOptions(project, [NormalizeBranch(project.Branch)]);
        BranchStatusText.Text = "Rama guardada localmente. Pulsa Actualizar para consultar Steamworks.";
        RefreshBranchesButton.IsEnabled = true;
        BranchComboBox.Text = NormalizeBranch(project.Branch);
        BuildCacheTextBox.Text = project.BuildOutput;
        VdfOutputTextBox.Text = project.VdfOutput;
        WindowsCheckBox.IsChecked = project.Platforms.Windows;
        LinuxCheckBox.IsChecked = project.Platforms.Linux;
        MacosCheckBox.IsChecked = project.Platforms.Macos;
        WindowsDepotIdTextBox.Text = GetDepot(project, "windows")?.DepotId ?? string.Empty;
        LinuxDepotIdTextBox.Text = GetDepot(project, "linux")?.DepotId ?? string.Empty;
        MacosDepotIdTextBox.Text = GetDepot(project, "macos")?.DepotId ?? string.Empty;
        WindowsPathTextBox.Text = GetDepot(project, "windows")?.ContentRoot ?? string.Empty;
        LinuxPathTextBox.Text = GetDepot(project, "linux")?.ContentRoot ?? string.Empty;
        MacosPathTextBox.Text = GetDepot(project, "macos")?.ContentRoot ?? string.Empty;
        RefreshVdfPreview();
        UpdatePreflight(project);

        _isBindingProject = false;
    }

    private void EnsureProjectDefaults(SteamProject project)
    {
        if (string.IsNullOrWhiteSpace(project.Branch))
        {
            project.Branch = _userConfig.DefaultBranch;
        }

        if (string.IsNullOrWhiteSpace(project.ContentRoot))
        {
            project.ContentRoot = $"output/{project.AppId}";
        }

        if (string.IsNullOrWhiteSpace(project.BuildOutput))
        {
            project.BuildOutput = $"steampipe/cache/{project.AppId}";
        }

        if (string.IsNullOrWhiteSpace(project.VdfOutput))
        {
            project.VdfOutput = "vdf/generated";
        }

        EnsureDepot(project, "windows");
        EnsureDepot(project, "linux");
        EnsureDepot(project, "macos");
    }

    private static DepotDefinition EnsureDepot(SteamProject project, string platform)
    {
        var depot = GetDepot(project, platform);
        if (depot is not null)
        {
            if (string.IsNullOrWhiteSpace(depot.ContentRoot))
            {
                depot.ContentRoot = $"output/{project.AppId}/{platform}";
            }

            return depot;
        }

        depot = new DepotDefinition
        {
            DepotId = string.Empty,
            Name = platform,
            Platform = platform,
            ContentRoot = $"output/{project.AppId}/{platform}"
        };

        project.Depots.Add(depot);
        return depot;
    }

    private static DepotDefinition? GetDepot(SteamProject project, string platform)
    {
        return project.Depots.FirstOrDefault(d => d.Platform.Equals(platform, StringComparison.OrdinalIgnoreCase));
    }

    private void ProjectField_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isBindingProject || SelectedProject is not { } project)
        {
            return;
        }

        project.AppId = AppIdTextBox.Text.Trim();
        project.BuildOutput = BuildCacheTextBox.Text.Trim();
        project.VdfOutput = VdfOutputTextBox.Text.Trim();
        _userConfig.LastProjectAppId = project.AppId;
        HeaderAppIdText.Text = $"AppID: {project.AppId}";
        RefreshVdfPreview();
        UpdatePreflight(project);
        ScheduleSave();
    }

    private void BranchComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateBranchFromUi();
    }

    private void BranchComboBox_LostFocus(object sender, RoutedEventArgs e)
    {
        UpdateBranchFromUi();
    }

    private void UpdateBranchFromUi()
    {
        if (_isBindingProject || SelectedProject is not { } project)
        {
            return;
        }

        project.Branch = NormalizeBranch(BranchComboBox.Text);
        HeaderBranchText.Text = $"Branch: {project.Branch}";
        RefreshVdfPreview();
        UpdatePreflight(project);
        ScheduleSave();
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
        RefreshVdfPreview();
        UpdatePreflight(project);
        ScheduleSave();
    }

    private void PathTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isBindingProject || SelectedProject is not { } project)
        {
            return;
        }

        EnsureDepot(project, "windows").ContentRoot = WindowsPathTextBox.Text.Trim();
        EnsureDepot(project, "linux").ContentRoot = LinuxPathTextBox.Text.Trim();
        EnsureDepot(project, "macos").ContentRoot = MacosPathTextBox.Text.Trim();
        RefreshVdfPreview();
        UpdatePreflight(project);
        ScheduleSave();
    }

    private void DepotField_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isBindingProject || SelectedProject is not { } project)
        {
            return;
        }

        EnsureDepot(project, "windows").DepotId = WindowsDepotIdTextBox.Text.Trim();
        EnsureDepot(project, "linux").DepotId = LinuxDepotIdTextBox.Text.Trim();
        EnsureDepot(project, "macos").DepotId = MacosDepotIdTextBox.Text.Trim();
        RefreshVdfPreview();
        UpdatePreflight(project);
        ScheduleSave();
    }

    private void SettingsField_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isBindingProject || _isLoadingConfig)
        {
            return;
        }

        _userConfig.SteamCmdPath = SteamCmdPathTextBox.Text.Trim();
        _userConfig.Username = UsernameTextBox.Text.Trim();
        _userConfig.Steamworks.PartnerApiBaseUrl = SteamworksApiBaseUrlTextBox.Text.Trim();
        UpdateSteamCmdStatus(File.Exists(_steamCmdRunner.ResolveSteamCmdPath(_userConfig)));
        UpdateSteamworksStatusFromConfig();
        ScheduleSave();
    }

    private void SteamworksApiKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoadingConfig)
        {
            return;
        }

        _userConfig.Steamworks.PublisherApiKey = SteamworksApiKeyPasswordBox.Password;
        _userConfig.Steamworks.Enabled = !string.IsNullOrWhiteSpace(_userConfig.Steamworks.PublisherApiKey);
        UpdateSteamworksStatusFromConfig();
        ScheduleSave();
    }

    private async void SaveChangesButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _saveCts?.Cancel();
            CaptureSteamworksSettings();
            await SaveAllAsync();
        }
        catch (Exception ex)
        {
            AppendLog($"No se pudieron guardar los cambios: {ex.Message}");
            FooterStatusText.Text = "Error al guardar cambios.";
        }
    }

    private async Task SaveAllAsync()
    {
        CaptureSteamworksSettings();

        if (SelectedProject is { } project)
        {
            _userConfig.LastProjectAppId = project.AppId;
        }

        await _saveLock.WaitAsync();
        try
        {
            await _manifestService.SaveAsync(_paths.ManifestPath, _manifest);
            await SaveUserConfigFileAsync();
        }
        finally
        {
            _saveLock.Release();
        }

        AppendLog("Cambios guardados.");
    }

    private async Task SaveUserConfigAsync()
    {
        CaptureSteamworksSettings();
        _userConfig.AutoScrollLogs = AutoScrollCheckBox.IsChecked == true;
        await _saveLock.WaitAsync();
        try
        {
            await SaveUserConfigFileAsync();
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private Task SaveUserConfigFileAsync()
    {
        return _userConfigService.SaveAsync(_paths.UserConfigPath, _userConfig);
    }

    private void ScheduleSave()
    {
        if (_isBindingProject || _isLoadingConfig)
        {
            return;
        }

        _saveCts?.Cancel();
        var cts = new CancellationTokenSource();
        _saveCts = cts;
        _ = SaveAfterDelayAsync(cts);
    }

    private async Task SaveAfterDelayAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(350, cts.Token);
            await SaveAllAsync();
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            AppendLog($"Guardado automatico falló: {ex.Message}");
        }
        finally
        {
            if (ReferenceEquals(_saveCts, cts))
            {
                _saveCts = null;
            }

            cts.Dispose();
        }
    }

    private void CaptureSteamworksSettings()
    {
        _userConfig.Steamworks ??= new SteamworksConfig();
        _userConfig.Steamworks.PartnerApiBaseUrl = SteamworksApiBaseUrlTextBox.Text.Trim();
        _userConfig.Steamworks.PublisherApiKey = SteamworksApiKeyPasswordBox.Password;
        _userConfig.Steamworks.Enabled = !string.IsNullOrWhiteSpace(_userConfig.Steamworks.PublisherApiKey);
    }

    private void RefreshVdfPreview()
    {
        if (SelectedProject is not { } project)
        {
            return;
        }

        try
        {
            VdfPreviewTextBox.Text = _vdfGenerator.Preview(project);
        }
        catch (Exception ex)
        {
            VdfPreviewTextBox.Text = $"No se pudo generar preview: {ex.Message}";
        }
    }

    private void GenerateVdfButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProject is not { } project)
        {
            AppendLog("Selecciona un proyecto antes de generar VDF.");
            return;
        }

        var errors = _vdfGenerator.Validate(project);
        if (errors.Count > 0)
        {
            PreflightText.Text = string.Join(Environment.NewLine, errors);
            AppendLog("VDF no generado: corrige la validacion local primero.");
            return;
        }

        try
        {
            var files = _vdfGenerator.Generate(project);
            RefreshVdfPreview();
            AppendLog("VDF generado:");
            foreach (var file in files)
            {
                AppendLog($"  {file}");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"No se pudo generar el VDF: {ex.Message}");
        }
    }

    private void ValidateBuildButton_Click(object sender, RoutedEventArgs e)
    {
        ValidateSelectedBuild(showSuccess: true);
    }

    private bool ValidateSelectedBuild(bool showSuccess)
    {
        if (SelectedProject is not { } project)
        {
            AppendLog("Selecciona un proyecto antes de validar.");
            return false;
        }

        var errors = _vdfGenerator.Validate(project);
        if (errors.Count == 0)
        {
            PreflightText.Text = "Local validation passed. DepotIDs still need Steamworks verification.";
            if (showSuccess)
            {
                AppendLog("Validacion local OK. Los DepotIDs del manifest no se consideran verificados por Steamworks.");
            }

            return true;
        }

        PreflightText.Text = string.Join(Environment.NewLine, errors);
        foreach (var error in errors)
        {
            AppendLog($"Validacion: {error}");
        }

        return false;
    }

    private void CreateFoldersButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProject is not { } project)
        {
            AppendLog("Selecciona un proyecto antes de crear carpetas.");
            return;
        }

        try
        {
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

            UpdatePreflight(project);
        }
        catch (Exception ex)
        {
            AppendLog($"No se pudieron crear las carpetas: {ex.Message}");
        }
    }

    private async void CheckSteamCmdButton_Click(object sender, RoutedEventArgs e)
    {
        if (_operationInProgress)
        {
            return;
        }

        await RunOperationAsync(
            "SteamCMD check",
            async () =>
            {
                var exitCode = await _steamCmdRunner.CheckInstallationAsync(_userConfig, AppendSteamCmdOutput);
                UpdateSteamCmdStatus(exitCode == 0);
                return exitCode;
            });
    }

    private async void RefreshBranchesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_operationInProgress)
        {
            return;
        }

        await LoadBranchesForProjectAsync(SelectedProject);
    }

    private async void CheckSteamworksButton_Click(object sender, RoutedEventArgs e)
    {
        if (_operationInProgress)
        {
            return;
        }

        if (SelectedProject is not { } project)
        {
            AppendLog("Selecciona un proyecto antes de comprobar Steamworks.");
            return;
        }

        try
        {
            CaptureSteamworksSettings();
            await SaveUserConfigAsync();
            _operationInProgress = true;
            UploadBuildButton.IsEnabled = false;
            CheckSteamworksButton.IsEnabled = false;
            FooterStatusText.Text = "Comprobando Steamworks API...";
            SetSteamworksStatus("Checking", (Brush)FindResource("TextMuted"), new SolidColorBrush(Color.FromRgb(107, 119, 136)), "Comprobando...");

            var result = await _steamworksApiClient.CheckPublisherKeyAsync(_userConfig.Steamworks, project.AppId);
            AppendLog(result.Message);

            if (result.Success)
            {
                SetSteamworksStatus("Connected", (Brush)FindResource("AccentGreen"), (Brush)FindResource("AccentGreen"), result.Message);
                FooterStatusText.Text = "Steamworks API conectada.";
            }
            else
            {
                SetSteamworksStatus("Error", (Brush)FindResource("DangerRed"), (Brush)FindResource("DangerRed"), result.Message);
                FooterStatusText.Text = "La comprobación de Steamworks falló.";
            }
        }
        catch (Exception ex)
        {
            AppendLog($"Comprobación de Steamworks falló: {ex.Message}");
            SetSteamworksStatus("Error", (Brush)FindResource("DangerRed"), (Brush)FindResource("DangerRed"), ex.Message);
            FooterStatusText.Text = "La comprobación de Steamworks falló.";
        }
        finally
        {
            _operationInProgress = false;
            UploadBuildButton.IsEnabled = true;
            CheckSteamworksButton.IsEnabled = true;
        }
    }

    private async void LoginSteamCmdButton_Click(object sender, RoutedEventArgs e)
    {
        if (_operationInProgress)
        {
            return;
        }

        await RunOperationAsync(
            "SteamCMD login",
            () => _steamCmdRunner.LoginAsync(_userConfig, AppendSteamCmdOutput, credentials: GetSteamCmdCredentials()));
    }

    private async void UploadBuildButton_Click(object sender, RoutedEventArgs e)
    {
        if (_operationInProgress || SelectedProject is not { } project)
        {
            return;
        }

        try
        {
            _outputFolders.EnsureSelectedPlatformFolders(project);
            var validationOk = ValidateSelectedBuild(showSuccess: false);
            if (!validationOk)
            {
                AppendLog("Upload cancelado: corrige la validacion local primero.");
                return;
            }

            var files = _vdfGenerator.Generate(project);
            var appBuildPath = _vdfGenerator.GetAppBuildPath(project);
            var confirmation = BuildUploadConfirmation(project, appBuildPath);
            var result = MessageBox.Show(
                confirmation,
                "Confirmar Upload Build",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (result != MessageBoxResult.Yes)
            {
                AppendLog("Upload cancelado por el usuario.");
                return;
            }

            AppendLog("VDF listo para upload:");
            foreach (var file in files)
            {
                AppendLog($"  {file}");
            }

            await RunOperationAsync(
                "Upload Build",
                () => _steamCmdRunner.UploadBuildAsync(_userConfig, appBuildPath, AppendSteamCmdOutput, credentials: GetSteamCmdCredentials()));
        }
        catch (Exception ex)
        {
            AppendLog($"Upload cancelado: {ex.Message}");
            FooterStatusText.Text = "Upload cancelado por un error local.";
        }
    }

    private async Task RunOperationAsync(string label, Func<Task<int>> operation)
    {
        try
        {
            _operationInProgress = true;
            UploadBuildButton.IsEnabled = false;
            CheckSteamCmdButton.IsEnabled = false;
            LoginSteamCmdButton.IsEnabled = false;
            RefreshBranchesButton.IsEnabled = false;
            FooterStatusText.Text = $"{label} en progreso...";
            var exitCode = await operation();
            FooterStatusText.Text = exitCode == 0 ? $"{label} finalizado." : $"{label} finalizo con codigo {exitCode}.";
        }
        catch (Exception ex)
        {
            AppendLog($"{label} fallo: {ex.Message}");
            FooterStatusText.Text = $"{label} fallo.";
        }
        finally
        {
            _operationInProgress = false;
            UploadBuildButton.IsEnabled = true;
            CheckSteamCmdButton.IsEnabled = true;
            LoginSteamCmdButton.IsEnabled = true;
            RefreshBranchesButton.IsEnabled = SelectedProject is not null;
            SteamCmdPasswordBox.Clear();
            SteamGuardCodePasswordBox.Clear();
            SteamCmdManualInputBox.Clear();
        }
    }

    private string BuildUploadConfirmation(SteamProject project, string appBuildPath)
    {
        var selectedDepots = project.Depots
            .Where(d => project.Platforms.SelectedPlatforms().Contains(d.Platform, StringComparer.OrdinalIgnoreCase))
            .Select(d => $"{d.Platform}: DepotID {d.DepotId}, path {_paths.Resolve(d.ContentRoot)}");

        return "Estas a punto de ejecutar SteamCMD run_app_build.\n\n" +
               $"Proyecto: {project.Name}\n" +
               $"AppID: {project.AppId}\n" +
               $"Rama destino: {NormalizeBranch(project.Branch)}\n" +
               $"App build VDF: {appBuildPath}\n\n" +
               "Plataformas y depots:\n" +
               string.Join(Environment.NewLine, selectedDepots) +
               "\n\nNo se hara SetLive para la rama default. No continues si estos DepotIDs no fueron verificados en Steamworks.";
    }

    private SteamCmdCredentials? GetSteamCmdCredentials()
    {
        var password = SteamCmdPasswordBox.Password;
        var guardCode = SteamGuardCodePasswordBox.Password;
        return string.IsNullOrEmpty(password) && string.IsNullOrEmpty(guardCode)
            ? null
            : new SteamCmdCredentials(password, guardCode);
    }

    private void AutoScrollCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isBindingProject || _isLoadingConfig)
        {
            return;
        }

        _userConfig.AutoScrollLogs = AutoScrollCheckBox.IsChecked == true;
        ScheduleSave();
    }

    private void ClearLogsButton_Click(object sender, RoutedEventArgs e)
    {
        LogsTextBox.Clear();
        _steamCmdConsoleAtLineStart = true;
    }

    private void SendSteamCmdInputButton_Click(object sender, RoutedEventArgs e)
    {
        var input = SteamCmdManualInputBox.Password;
        if (_steamCmdRunner.TrySendInput(input, AppendSteamCmdOutput))
        {
            SteamCmdManualInputBox.Clear();
        }
    }

    private void UpdatePreflight(SteamProject project)
    {
        var errors = _vdfGenerator.Validate(project);
        PreflightText.Text = errors.Count == 0
            ? "Ready locally. DepotIDs are not Steamworks-verified."
            : string.Join(Environment.NewLine, errors.Take(4));
    }

    private void UpdateSteamCmdStatus(bool installed)
    {
        SteamCmdStatusText.Text = installed ? "Installed" : "Missing";
        SteamCmdStatusText.Foreground = installed
            ? (System.Windows.Media.Brush)FindResource("AccentGreen")
            : (System.Windows.Media.Brush)FindResource("DangerRed");
    }

    private void UpdateSteamworksStatusFromConfig()
    {
        var configured = _userConfig.Steamworks is { } steamworks &&
                         steamworks.Enabled &&
                         !string.IsNullOrWhiteSpace(steamworks.PublisherApiKey);

        if (configured)
        {
            SetSteamworksStatus("Configured", (Brush)FindResource("TextMuted"), (Brush)FindResource("AccentBlue"), "Key guardada localmente. Falta comprobarla.");
        }
        else
        {
            SetSteamworksStatus("Not configured", (Brush)FindResource("TextMuted"), new SolidColorBrush(Color.FromRgb(107, 119, 136)), "Introduce una Publisher Web API key.");
        }
    }

    private void SetSteamworksStatus(string header, Brush foreground, Brush dot, string details)
    {
        SteamworksStatusText.Text = header;
        SteamworksStatusText.Foreground = foreground;
        SteamworksStatusDot.Fill = dot;
        SteamworksConfigStatusText.Text = details;
        SteamworksConfigStatusText.Foreground = foreground;
    }

    private static string NormalizeBranch(string? branch)
    {
        return string.IsNullOrWhiteSpace(branch) ? "default" : branch.Trim();
    }

    private void AppendLog(string message)
    {
        Dispatcher.Invoke(() =>
        {
            LogsTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            if (AutoScrollCheckBox.IsChecked == true)
            {
                LogsTextBox.ScrollToEnd();
            }
        });
    }

    private void AppendSteamCmdOutput(string chunk)
    {
        if (string.IsNullOrEmpty(chunk))
        {
            return;
        }

        Dispatcher.Invoke(() =>
        {
            var normalized = chunk.Replace("\r\n", "\n").Replace('\r', '\n');
            var rendered = new StringBuilder(normalized.Length + 32);

            foreach (var character in normalized)
            {
                if (_steamCmdConsoleAtLineStart)
                {
                    rendered.Append('[').Append(DateTime.Now.ToString("HH:mm:ss")).Append("] ");
                    _steamCmdConsoleAtLineStart = false;
                }

                if (character == '\n')
                {
                    rendered.Append(Environment.NewLine);
                    _steamCmdConsoleAtLineStart = true;
                }
                else
                {
                    rendered.Append(character);
                }
            }

            LogsTextBox.AppendText(rendered.ToString());
            if (AutoScrollCheckBox.IsChecked == true)
            {
                LogsTextBox.ScrollToEnd();
            }
        });
    }
}
