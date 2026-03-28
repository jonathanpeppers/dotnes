using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using Microsoft.Build.Framework;

namespace dotnes;

/// <summary>
/// MSBuild task that downloads, verifies, and extracts Mesen.
/// Uses a named mutex to prevent parallel builds from racing.
/// Implements ICancelableTask so Ctrl+C aborts cleanly.
/// </summary>
public class EnsureMesenInstalled : Microsoft.Build.Utilities.Task, ICancelableTask
{
    CancellationTokenSource _cts = new();

    [Required]
    public string MesenExe { get; set; } = "";

    [Required]
    public string DownloadUrl { get; set; } = "";

    [Required]
    public string LicenseUrl { get; set; } = "";

    [Required]
    public string DestDir { get; set; } = "";

    [Required]
    public string ZipName { get; set; } = "";

    [Required]
    public string ExpectedHash { get; set; } = "";

    public int Retries { get; set; } = 3;

    public int RetryDelayMilliseconds { get; set; } = 2000;

    public void Cancel() => _cts.Cancel();

    public override bool Execute()
    {
        if (File.Exists(MesenExe))
            return true;

        using var mutex = new Mutex(false, "dotnes_mesen_download");
        try
        {
            mutex.WaitOne();
        }
        catch (AbandonedMutexException)
        {
            // Previous process crashed while holding the lock — safe to proceed
            Log.LogMessage(MessageImportance.Normal, "Acquired abandoned mutex, proceeding.");
        }

        try
        {
            // Re-check after lock — another build may have finished
            if (File.Exists(MesenExe))
            {
                Log.LogMessage(MessageImportance.Normal, "Mesen already installed at {0}.", MesenExe);
                return true;
            }

            Directory.CreateDirectory(DestDir);
            var zipPath = Path.Combine(DestDir, ZipName);

            DownloadWithRetries(DownloadUrl, zipPath);

            if (!VerifySha256(zipPath))
                return false;

            Extract(zipPath);

            // License (best-effort)
            try
            {
                var licensePath = Path.Combine(DestDir, "LICENSE");
                DownloadWithRetries(LicenseUrl, licensePath);
                Log.LogMessage(MessageImportance.Normal, "Downloaded LICENSE to {0}.", licensePath);
            }
            catch (Exception ex)
            {
                Log.LogMessage(MessageImportance.Normal, "License download skipped: {0}", ex.Message);
            }

            if (!File.Exists(MesenExe))
            {
                Log.LogError("Mesen executable not found after extraction: {0}", MesenExe);
                return false;
            }

            Log.LogMessage(MessageImportance.High, "Mesen ready at {0}.", MesenExe);
            return true;
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    void DownloadWithRetries(string url, string dest)
    {
        for (int attempt = 1; attempt <= Retries; attempt++)
        {
            _cts.Token.ThrowIfCancellationRequested();
            try
            {
                Log.LogMessage(MessageImportance.High, "Downloading {0} (attempt {1}/{2})...", url, attempt, Retries);
                using var handler = new HttpClientHandler { AllowAutoRedirect = true };
                using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
                using var response = http.GetAsync(url, _cts.Token).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();
                Log.LogMessage(MessageImportance.Normal, "Response: {0}, Content-Length: {1}",
                    response.StatusCode, response.Content.Headers.ContentLength);

                using var content = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
                using var fs = File.Create(dest);
                content.CopyTo(fs);
                Log.LogMessage(MessageImportance.Normal, "Downloaded {0} bytes to {1}.", fs.Length, dest);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < Retries)
            {
                Log.LogMessage(MessageImportance.High, "Download failed (attempt {0}/{1}): {2}", attempt, Retries, ex.Message);
                Thread.Sleep(RetryDelayMilliseconds);
            }
        }
    }

    bool VerifySha256(string path)
    {
        Log.LogMessage(MessageImportance.Normal, "Verifying SHA256 of {0}...", path);
        using var fs = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "");
        if (!hash.Equals(ExpectedHash, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(path);
            Log.LogError("SHA256 mismatch for {0}: expected {1}, got {2}", path, ExpectedHash, hash);
            return false;
        }
        Log.LogMessage(MessageImportance.Normal, "SHA256 verified: {0}", hash);
        return true;
    }

    void Extract(string zipPath)
    {
        Log.LogMessage(MessageImportance.High, "Extracting {0}...", zipPath);
        using (var archive = ZipFile.OpenRead(zipPath))
        {
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                    continue;
                var destPath = Path.Combine(DestDir, entry.FullName);
                var destDir = Path.GetDirectoryName(destPath)!;
                Directory.CreateDirectory(destDir);
                entry.ExtractToFile(destPath, overwrite: true);
            }
        }
        File.Delete(zipPath);

        // macOS: Mesen ships as a nested zip (Mesen.app.zip inside the outer zip)
        var nestedZip = Path.Combine(DestDir, "Mesen.app.zip");
        if (File.Exists(nestedZip))
        {
            Log.LogMessage(MessageImportance.Normal, "Extracting nested {0}...", nestedZip);
            using (var archive = ZipFile.OpenRead(nestedZip))
            {
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name))
                        continue;
                    var destPath = Path.Combine(DestDir, entry.FullName);
                    var destDir = Path.GetDirectoryName(destPath)!;
                    Directory.CreateDirectory(destDir);
                    entry.ExtractToFile(destPath, overwrite: true);
                }
            }
            File.Delete(nestedZip);
        }
    }
}
