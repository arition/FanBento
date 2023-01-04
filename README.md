# FanBento

Fetch the posts in Pixiv Fanbox.

Documentation WIP.

## How To Use
Download the latest release files.

### FanBento.Fetch

#### Settings

Put the `settings.json` with the following data under the working directory.

```json
{
  "Database": {
    "ConnectionString": "Data Source=/path/to/the/db/file;" //does not have to exist
  },
  "Fanbox": {
    "FanboxSessionId": "fanbox_session_id_from_your_browser_cookie" //in the form of \d{8}_\w{32}
  },
  "Assets": {
    "Storage": "", //can be "S3" or "FileSystem"
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

#### Start

```bash
dotnet FanBento.Fetch.dll
```

### FanBento.Website

#### Settings

Put the `appsettings.Production.json` (or `appsettings.Development.json`) with the following data under the working directory.

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

#### Start

Put the `imgs` and `files` directories downloaded under `FanBento.Website/wwwroot`

```bash
dotnet FanBento.Website.dll
```

Optionally, set environment variable `ASPNETCORE_ENVIRONMENT=“Development”` to show an index of posts at `/posts`. When doing so, put settings in ```appsettings.Development.json```.
You can also pass the options `--urls` to specify which URL to listen to, by default it only listens to `localhost:5000`.
For example
```bash
#Liten on all addresses
dotnet FanBento.Website.dll --urls=http://0.0.0.0:5000
```


### FanBento.TelegramBot

#### Settings

Put the `settings.json` with the following data under the working directory.

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

#### Start

```bash
dotnet FanBento.TelegramBot.dll
```

## Database Migrations

WIP

## How To Build