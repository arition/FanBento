using System;
using System.IO;
using System.Threading.Tasks;
using Anotar.Serilog;
using Microsoft.Playwright;
using Microsoft.Win32.SafeHandles;

namespace FanBento.Fetch.Api;

internal class AoiroboxApi
{
    public async Task<(string, Stream, string)> GetDownloadFileStream(string url)
    {
        using var playwright = await Playwright.CreateAsync();

        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            DownloadsPath = Path.GetTempPath()
        });
        var context = await browser.NewContextAsync();
        // Open new page
        var page = await context.NewPageAsync();
        // Navigate to the page
        await page.GotoAsync(url, new PageGotoOptions { Referer = "https://www.fanbox.cc/" });
        // Click text=Click!!
        await page.Locator("text=Click!!").ClickAsync();

        // try wait for download task
        var waitForDownloadTask = page.WaitForDownloadAsync();

        var type = "file";
        try
        {
            await Task.Delay(1000);
            if (page.Url.StartsWith("https://aoirobox.sakura.ne.jp/app/img/")) LogTo.Warning("Open PDF viewer");
            await page.WaitForSelectorAsync("#shadow",
                new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 10000 });
            await page.EvaluateAsync("document.querySelector('#shadow').download = 'download'");
        }
        catch
        {
            await page.WaitForSelectorAsync("img.img-fluid",
                new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 10000 });
            await page.EvaluateAsync("const anchor = document.createElement('a');" +
                                     "anchor.download='download';" +
                                     "anchor.href=document.querySelector('img.img-fluid').src;" +
                                     "document.body.appendChild(anchor); " +
                                     "anchor.click();");
            type = "image";
        }

        var download = await waitForDownloadTask;
        var path = await download.PathAsync();

        var stream =
            new DeleteFileStream(path ?? throw new InvalidOperationException(), FileMode.Open, FileAccess.Read);
        return (download.Url, stream, type);
    }
}

public class DeleteFileStream : FileStream
{
    public DeleteFileStream(SafeFileHandle handle, FileAccess access) : base(handle, access)
    {
    }

    public DeleteFileStream(SafeFileHandle handle, FileAccess access, int bufferSize) : base(handle, access, bufferSize)
    {
    }

    public DeleteFileStream(SafeFileHandle handle, FileAccess access, int bufferSize, bool isAsync) : base(handle,
        access, bufferSize, isAsync)
    {
    }

    public DeleteFileStream(string path, FileMode mode) : base(path, mode)
    {
    }

    public DeleteFileStream(string path, FileMode mode, FileAccess access) : base(path, mode, access)
    {
    }

    public DeleteFileStream(string path, FileMode mode, FileAccess access, FileShare share) : base(path, mode, access,
        share)
    {
    }

    public DeleteFileStream(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize) : base(path,
        mode, access, share, bufferSize)
    {
    }

    public DeleteFileStream(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize,
        bool useAsync) : base(path, mode, access, share, bufferSize, useAsync)
    {
    }

    public DeleteFileStream(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize,
        FileOptions options) : base(path, mode, access, share, bufferSize, options)
    {
    }

    public DeleteFileStream(string path, FileStreamOptions options) : base(path, options)
    {
    }

    public override void Close()
    {
        base.Close();
        File.Delete(Name);
    }
}