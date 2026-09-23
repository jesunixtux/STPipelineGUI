acá se pone las configuraciones hasta las cookies y tambien alas apis que se ocuparian,
(Recomendacion opcional aunque permitamos usarlos asi sin seguridad permitamos que se pueda agregar un sistema para evitar que se vea las apis sensibles, es opcional pero puedes tenerlo en mente)

Archivo local opcional esperado por la app:

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

`config/user.json` queda ignorado por Git. La key se muestra enmascarada en la GUI, se envía solo por HTTPS y nunca se escribe en los logs. Steamworks debe tener el AppID asociado al grupo de la Publisher Web API key.
