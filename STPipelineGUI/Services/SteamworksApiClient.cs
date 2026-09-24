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
        if (string.IsNullOrWhiteSpace(config.PublisherApiKey))
        {
            return new SteamworksCheckResult(false, false, "No hay una Publisher Web API key configurada.");
        }

        if (!config.Enabled)
        {
            return new SteamworksCheckResult(false, false, "La integración de Steamworks está desactivada en la configuración.");
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

        var endpoint = BuildEndpoint(
            baseUri,
            "ISteamApps/GetPartnerAppListForWebAPIKey/v2/",
            new Dictionary<string, string>
            {
                ["key"] = config.PublisherApiKey.Trim(),
                ["type_filter"] = "game,application,tool,demo,dlc,music"
            });

        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Accept.ParseAdd("application/json");

        try
        {
            using var response = await HttpClient.SendAsync(request, cancellationToken);
            var statusCode = (int)response.StatusCode;

            if (!response.IsSuccessStatusCode)
            {
                return new SteamworksCheckResult(
                    false,
                    false,
                    DescribeHttpFailure(statusCode, "la comprobación"),
                    statusCode);
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
            var apps = FindApps(document.RootElement);

            if (apps is null)
            {
            return new SteamworksCheckResult(false, false, "Steamworks devolvió una respuesta de aplicaciones no reconocida.", statusCode);
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
        catch (HttpRequestException)
        {
            return new SteamworksCheckResult(false, false, "No se pudo conectar con Steamworks. Comprueba la URL, tu red y que la key tenga acceso al AppID.");
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
        if (string.IsNullOrWhiteSpace(config.PublisherApiKey))
        {
            return new SteamworksBranchesResult(false, [], "No hay una Publisher Web API key configurada.");
        }

        if (!config.Enabled)
        {
            return new SteamworksBranchesResult(false, [], "La integración de Steamworks está desactivada en la configuración.");
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

        var endpoint = BuildEndpoint(
            baseUri,
            "ISteamApps/GetAppBetas/v1/",
            new Dictionary<string, string>
            {
                ["key"] = config.PublisherApiKey.Trim(),
                ["appid"] = numericAppId.ToString()
            });

        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Accept.ParseAdd("application/json");

        try
        {
            using var response = await HttpClient.SendAsync(request, cancellationToken);
            var statusCode = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                return new SteamworksBranchesResult(
                    false,
                    [],
                    DescribeHttpFailure(statusCode, "la consulta de ramas"),
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
        catch (HttpRequestException)
        {
            return new SteamworksBranchesResult(false, [], "No se pudo consultar Steamworks. Comprueba la URL, tu red y que la key tenga acceso al AppID.");
        }
        catch (JsonException)
        {
            return new SteamworksBranchesResult(false, [], "Steamworks devolvió una respuesta JSON no válida para las ramas.");
        }
    }

    private static IReadOnlyCollection<uint>? FindApps(JsonElement root)
    {
        root = UnwrapResponse(root);
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
        root = UnwrapResponse(root);
        if (!root.TryGetProperty("betas", out var betas) &&
            !root.TryGetProperty("branches", out betas))
        {
            return null;
        }

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectBranchNames(betas, result);
        return result.OrderBy(branch => branch, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void CollectBranchNames(JsonElement value, ISet<string> result)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                CollectBranchNames(item, result);
            }

            return;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (value.TryGetProperty("name", out var name) &&
            name.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(name.GetString()))
        {
            result.Add(name.GetString()!.Trim());
            return;
        }

        if (value.TryGetProperty("beta", out var betaItems))
        {
            CollectBranchNames(betaItems, result);
            return;
        }

        foreach (var property in value.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Array ||
                (property.Value.ValueKind == JsonValueKind.Object &&
                 (property.Value.TryGetProperty("name", out _) || property.Value.TryGetProperty("beta", out _))))
            {
                CollectBranchNames(property.Value, result);
            }
            else if (!string.IsNullOrWhiteSpace(property.Name))
            {
                result.Add(property.Name.Trim());
            }
        }
    }

    private static JsonElement UnwrapResponse(JsonElement root)
    {
        return root.ValueKind == JsonValueKind.Object &&
               root.TryGetProperty("response", out var response) &&
               response.ValueKind == JsonValueKind.Object
            ? response
            : root;
    }

    private static Uri BuildEndpoint(Uri baseUri, string path, IReadOnlyDictionary<string, string> query)
    {
        var builder = new UriBuilder(baseUri)
        {
            Path = path.TrimStart('/')
        };

        builder.Query = string.Join("&", query.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return builder.Uri;
    }

    private static string DescribeHttpFailure(int statusCode, string operation)
    {
        return statusCode switch
        {
            401 => $"Steamworks rechazó {operation} (HTTP 401). La Publisher Web API key no es válida.",
            403 => $"Steamworks rechazó {operation} (HTTP 403). La key no tiene permiso para este AppID.",
            404 => $"Steamworks no encontró el endpoint durante {operation} (HTTP 404). Revisa la URL de la API.",
            429 => $"Steamworks limitó {operation} (HTTP 429). Espera un momento e inténtalo otra vez.",
            500 => $"Steamworks devolvió un error interno (HTTP 500) durante {operation}. La configuración local se conserva y puedes escribir la rama manualmente.",
            _ => $"Steamworks rechazó {operation} (HTTP {statusCode}). Revisa la key, sus permisos y el AppID."
        };
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
