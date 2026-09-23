# STPipelineGUI

Aplicación WPF para preparar builds de SteamPipe desde proyectos configurados por el usuario.

## Estado MVP

- Carga `json/manifest.json`.
- Carga `config/user.json` si existe, sin exigir credenciales.
- Permite agregar proyectos desde la GUI, sin ejemplos preconfigurados.
- Muestra proyectos, datos principales, DepotIDs y plataformas.
- Crea `output/{AppID}/{windows|linux|macos}` solo para plataformas seleccionadas.
- Genera VDF basicos en `vdf/generated`.
- Separa comprobacion de SteamCMD, login, validacion local, generacion VDF y upload.
- Ejecuta SteamCMD y captura salida y errores en tiempo real.
- Mantiene password y Steam Guard solo en memoria durante login/upload.
- Consulta las ramas del AppID desde Steamworks cuando la Publisher API key tiene acceso.
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

- Sincronizacion amplia con Steamworks API.
- Verificacion remota de DepotIDs y builds.
- Historial persistente de builds.
