# FanBento

Fetch the posts in pixiv Fanbox.

Documentation WIP.

## FanBento.Fetch

### Settings

Put the ```settings.json``` with following data under the working directory.

```json
{
  "Database": {
    "ConnectionString": "Data Source=/path/to/the/db/file;"
  },
  "Fanbox": {
    "FanboxSessionId": "fanbox_session_id_from_your_browser_cookie",
    "ImageSavePath": "imgs/save/path",
    "FileSavePath": "files/save/path"
  },
  "Serilog": {
    "Using": ["Serilog.Sinks.Console"],
    "MinimumLevel": "Information",
    "WriteTo": [
      {
        "Name": "Console",
        "Args": {
          "outputTemplate": "[{Timestamp:HH:mm:ss} {Level:u3}] [Line: {LineNumber}, Method: {MethodName}, Class: {SourceContext:l}] {Message:lj}{NewLine}{Exception}"
        } 
      }
    ]
  }
}
```

### Start

```bash
dotnet FanBento.Fetch.dll
```

## FanBento.Website

Put the ```appsettings.Production.json``` with following data under the working directory.

```json
{
  "Database": {
    "ConnectionString": "Data Source=/path/to/the/db/file;"
  }
}
```

### Start

```bash
dotnet FanBento.Website.dll
```

## Database Migrations

WIP

