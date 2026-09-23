# STPipelineGUI

Base WPF para preparar builds de SteamPipe desde un manifest local.

## Estado MVP

- Carga `json/manifest.json`.
- Carga `config/user.json` si existe, sin exigir credenciales.
- Muestra proyectos, datos principales, depots y plataformas.
- Crea `output/{AppID}/{windows|linux|macos}` solo para plataformas seleccionadas.
- Genera VDF basicos en `vdf/generated`.
- Separa comprobacion de SteamCMD, login, validacion local, generacion VDF y upload.
- Deja preparado `SteamCmdRunner` para ejecutar `steamcmd.exe` y capturar salida en tiempo real.
- Muestra logs dentro de la GUI.
- Pide confirmacion antes de ejecutar un upload real.

## Ejecutar

```powershell
dotnet run --project .\STPipelineGUI\STPipelineGUI.csproj
```

## Config local opcional

`config/user.json` esta ignorado por Git. Ejemplo:

```json
{
  "steamCmdPath": "steamcmd/SteamCMD/steamcmd.exe",
  "username": "",
  "defaultBranch": "default",
  "lastProjectAppId": "",
  "autoScrollLogs": true,
  "steamworks": {
    "enabled": true,
    "publisherApiKey": "TU_PUBLISHER_KEY",
    "partnerApiBaseUrl": "https://partner.steam-api.com"
  }
}
```

La key se guarda únicamente en `config/user.json`, que está ignorado por Git. Desde Settings puedes comprobar que la key esté asociada al AppID del proyecto seleccionado. No se muestra en los logs ni se envía por una URL.

## Pendiente de integracion

- Login completo con Steam Guard/2FA dentro de la app.
- Sincronizacion amplia con Steamworks API.
- Verificacion remota de DepotIDs y builds.
- Historial persistente de builds.
