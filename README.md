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
    "FanboxSessionId": "fanbox_session_id_from_your_browser_cookie"
  },
  "Assets": {
    "Storage": "can be S3 or FileSystem",
    "S3": {
      "Endpoint": "s3 endpoint",
      "Bucket": "fanbento",
      "ImageSavePath": "imgs",
      "FileSavePath": "files",
      "KeyId": "your-accress-key-id",
      "KeySecret": "your-secret-access-key"
    },
    "FileSystem": {
      "ImageSavePath": "save/path/imgs",
      "FileSavePath": "save/path/files"
    }
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

### Settings

Put the ```appsettings.Production.json``` with following data under the working directory.

```json
{
  "Database": {
    "ConnectionString": "Data Source=/path/to/the/db/file;"
  },
  "Assets": {
    "UrlPrefix": "https://your-assets-domain.com (can be empty)"
  }
}
```

### Start

```bash
dotnet FanBento.Website.dll
```

## FanBento.TelegramBot

### Settings

Put the ```settings.json``` with following data under the working directory.

```json
{
  "Database": {
    "ConnectionString": "Data Source=/path/to/the/db/file;"
  },
  "Telegram": {
    "BotToken": "telegram bot token",
    "ChannelId": "telegram channel id"
  },
  "FanBento.Website": {
    "Domain": "website domain"
  },
  "Serilog": {
    "Using": [ "Serilog.Sinks.Console" ],
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
dotnet FanBento.TelegramBot.dll
```

## Database Migrations

WIP

