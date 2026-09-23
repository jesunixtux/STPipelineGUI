using System.Net.Http;
using System.Text.Json;
using STPipelineGUI.Models;

namespace STPipelineGUI.Services;

public sealed record SteamworksCheckResult(
    bool Success,
    bool AppAssociated,
    string Message,
    int? StatusCode = null);

public sealed record SteamworksBranchesResult(
    bool Success,
    IReadOnlyList<string> Branches,
    string Message,
    int? StatusCode = null);

public sealed class SteamworksApiClient
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    public async Task<SteamworksCheckResult> CheckPublisherKeyAsync(
        SteamworksConfig config,
        string appId,
        CancellationToken cancellationToken = default)
    {
        if (!config.Enabled)
        {
            return new SteamworksCheckResult(false, false, "La integración de Steamworks está desactivada en la configuración.");
        }

        if (string.IsNullOrWhiteSpace(config.PublisherApiKey))
        {
            return new SteamworksCheckResult(false, false, "No hay una Publisher Web API key configurada.");
        }

        if (!Uri.TryCreate(config.PartnerApiBaseUrl, UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme != Uri.UriSchemeHttps)
        {
            return new SteamworksCheckResult(false, false, "La URL de Steamworks debe usar HTTPS.");
        }

        if (!uint.TryParse(appId, out var numericAppId))
        {
            return new SteamworksCheckResult(false, false, $"El AppID '{appId}' no es válido.");
        }

        var endpoint = new Uri(
            baseUri,
            "/ISteamApps/GetPartnerAppListForWebAPIKey/v2/?type_filter=game,application,tool,demo,dlc,music");

        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.TryAddWithoutValidation("x-webapi-key", config.PublisherApiKey.Trim());

        try
        {
            using var response = await HttpClient.SendAsync(request, cancellationToken);
            var statusCode = (int)response.StatusCode;

            if (!response.IsSuccessStatusCode)
            {
                return new SteamworksCheckResult(
                    false,
                    false,
                    $"Steamworks rechazó la comprobación (HTTP {statusCode}). Revisa la key, sus permisos y el grupo asociado.",
                    statusCode);
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
            var apps = FindApps(document.RootElement);

            if (apps is null)
            {
                return new SteamworksCheckResult(false, false, "Steamworks devolvió una respuesta que la aplicación no reconoce.", statusCode);
            }

            var associated = apps.Any(app => app == numericAppId);
            return associated
                ? new SteamworksCheckResult(true, true, $"Steamworks conectado. La key tiene acceso al AppID {numericAppId}.", statusCode)
                : new SteamworksCheckResult(false, false, $"La key respondió, pero no está asociada al AppID {numericAppId}.", statusCode);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new SteamworksCheckResult(false, false, "La comprobación de Steamworks agotó el tiempo de espera.");
        }
        catch (HttpRequestException ex)
        {
            return new SteamworksCheckResult(false, false, $"No se pudo conectar con Steamworks: {ex.Message}");
        }
        catch (JsonException)
        {
            return new SteamworksCheckResult(false, false, "Steamworks devolvió una respuesta JSON no válida.");
        }
    }

    public async Task<SteamworksBranchesResult> GetBranchesAsync(
        SteamworksConfig config,
        string appId,
        CancellationToken cancellationToken = default)
    {
        if (!config.Enabled)
        {
            return new SteamworksBranchesResult(false, [], "La integración de Steamworks está desactivada en la configuración.");
        }

        if (string.IsNullOrWhiteSpace(config.PublisherApiKey))
        {
            return new SteamworksBranchesResult(false, [], "No hay una Publisher Web API key configurada.");
        }

        if (!Uri.TryCreate(config.PartnerApiBaseUrl, UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme != Uri.UriSchemeHttps)
        {
            return new SteamworksBranchesResult(false, [], "La URL de Steamworks debe usar HTTPS.");
        }

        if (!uint.TryParse(appId, out var numericAppId))
        {
            return new SteamworksBranchesResult(false, [], $"El AppID '{appId}' no es válido.");
        }

        var endpoint = new Uri(baseUri, $"/ISteamApps/GetAppBetas/v1/?appid={numericAppId}");
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.TryAddWithoutValidation("x-webapi-key", config.PublisherApiKey.Trim());

        try
        {
            using var response = await HttpClient.SendAsync(request, cancellationToken);
            var statusCode = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                return new SteamworksBranchesResult(
                    false,
                    [],
                    $"Steamworks rechazó la consulta de ramas (HTTP {statusCode}). Revisa la key y el AppID.",
                    statusCode);
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
            var branches = FindBranches(document.RootElement);
            if (branches is null)
            {
                return new SteamworksBranchesResult(false, [], "Steamworks devolvió una respuesta de ramas no reconocida.", statusCode);
            }

            return new SteamworksBranchesResult(true, branches, $"Steamworks devolvió {branches.Count} rama(s) para el AppID {numericAppId}.", statusCode);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new SteamworksBranchesResult(false, [], "La consulta de ramas agotó el tiempo de espera.");
        }
        catch (HttpRequestException ex)
        {
            return new SteamworksBranchesResult(false, [], $"No se pudo consultar Steamworks: {ex.Message}");
        }
        catch (JsonException)
        {
            return new SteamworksBranchesResult(false, [], "Steamworks devolvió una respuesta JSON no válida para las ramas.");
        }
    }

    private static IReadOnlyCollection<uint>? FindApps(JsonElement root)
    {
        if (!root.TryGetProperty("applist", out var appList) ||
            !appList.TryGetProperty("apps", out var apps) ||
            !apps.TryGetProperty("app", out var appItems))
        {
            return null;
        }

        var result = new List<uint>();
        if (appItems.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in appItems.EnumerateArray())
            {
                AddAppId(item, result);
            }
        }
        else if (appItems.ValueKind == JsonValueKind.Object)
        {
            AddAppId(appItems, result);
        }

        return result;
    }

    private static IReadOnlyList<string>? FindBranches(JsonElement root)
    {
        if (!root.TryGetProperty("betas", out var betas) ||
            !betas.TryGetProperty("beta", out var betaItems))
        {
            return null;
        }

        var result = new List<string>();
        if (betaItems.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in betaItems.EnumerateArray())
            {
                AddBranchName(item, result);
            }
        }
        else if (betaItems.ValueKind == JsonValueKind.Object)
        {
            AddBranchName(betaItems, result);
        }

        return result;
    }

    private static void AddBranchName(JsonElement beta, ICollection<string> result)
    {
        if (beta.TryGetProperty("name", out var name) &&
            name.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(name.GetString()))
        {
            result.Add(name.GetString()!);
        }
    }

    private static void AddAppId(JsonElement app, ICollection<uint> result)
    {
        if (!app.TryGetProperty("appid", out var appId))
        {
            return;
        }

        if (appId.ValueKind == JsonValueKind.Number && appId.TryGetUInt32(out var numericAppId))
        {
            result.Add(numericAppId);
        }
        else if (appId.ValueKind == JsonValueKind.String && uint.TryParse(appId.GetString(), out numericAppId))
        {
            result.Add(numericAppId);
        }
    }
}
