using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Anotar.Serilog;
using Microsoft.Playwright;
using Microsoft.Win32.SafeHandles;

namespace FanBento.Fetch.Api;

internal class AoiroboxApi
{
    public async Task<List<(string, Stream, string)>> GetDownloadFileStream(string url)
    {
        LogTo.Information($"Start browser to download aoirobox item: {url}");
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

        try
        {
            return await GetPDFDownload(page, waitForDownloadTask);
        }
        catch
        {
            // PDF download failed, try to download images
        }

        return await GetImageDownload(page, waitForDownloadTask);
    }

    private async Task<List<(string, Stream, string)>> GetPDFDownload(IPage page, Task<IDownload> waitForDownloadTask)
    {
        var type = "file";
        await Task.Delay(1000);
        if (page.Url.StartsWith("https://aoirobox.sakura.ne.jp/app/img/")) LogTo.Warning("Open PDF viewer");
        await page.WaitForSelectorAsync("#shadow",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 10000 });
        await page.EvaluateAsync("document.querySelector('#shadow').download = 'download'");

        var download = await waitForDownloadTask;
        var path = await download.PathAsync();
        LogTo.Information($"Downloaded file from aoirobox: {download.Url}");

        var stream =
            new DeleteFileStream(path ?? throw new InvalidOperationException(), FileMode.Open, FileAccess.Read);
        return new List<(string, Stream, string)> { (download.Url, stream, type) };
    }

    private async Task<List<(string, Stream, string)>> GetImageDownload(IPage page, Task<IDownload> waitForDownloadTask)
    {
        await page.WaitForSelectorAsync("img.img-fluid",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 10000 });
        var images = page.Locator("img.img-fluid");
        var imagesCount = await images.CountAsync();
        var results = new List<(string, Stream, string)>();

        for (var i = 0; i < imagesCount; i++)
        {
            var type = "image";
            var imageUrl = await images.Nth(i).GetAttributeAsync("src");
            await page.EvaluateAsync("const anchor = document.createElement('a');" +
                                     "anchor.download='download';" +
                                     $"anchor.href='{imageUrl}';" +
                                     "document.body.appendChild(anchor); " +
                                     "anchor.click();");


            var download = await waitForDownloadTask;
            var path = await download.PathAsync();
            LogTo.Information($"Downloaded file from aoirobox: {download.Url}");

            var stream =
                new DeleteFileStream(path ?? throw new InvalidOperationException(), FileMode.Open, FileAccess.Read);
            results.Add((download.Url, stream, type));

            if (i != imagesCount - 1)
            {
                // reset wait for download task
                waitForDownloadTask = page.WaitForDownloadAsync();
            }
        }

        return results;
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