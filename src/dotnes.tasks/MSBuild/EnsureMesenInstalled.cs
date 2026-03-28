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
/// </summary>
public class EnsureMesenInstalled : Microsoft.Build.Utilities.Task
{
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

    static readonly HttpClient s_http = new(new HttpClientHandler { AllowAutoRedirect = true })
    {
        Timeout = TimeSpan.FromMinutes(10),
    };

    public override bool Execute()
    {
        if (File.Exists(MesenExe))
            return true;

        using var mutex = new Mutex(false, "dotnes_mesen_download");
        mutex.WaitOne();
        try
        {
            // Re-check after lock — another project may have finished
            if (File.Exists(MesenExe))
            {
                Log.LogMessage(MessageImportance.Normal, "Mesen already installed.");
                return true;
            }

            Directory.CreateDirectory(DestDir);
            var zipPath = Path.Combine(DestDir, ZipName);

            Log.LogMessage(MessageImportance.High, "Downloading Mesen...");
            DownloadFile(DownloadUrl, zipPath);

            if (!VerifySha256(zipPath))
                return false;

            // License (best-effort)
            try { DownloadFile(LicenseUrl, Path.Combine(DestDir, "LICENSE")); }
            catch { }

            Log.LogMessage(MessageImportance.High, "Extracting...");
            ZipFile.ExtractToDirectory(zipPath, DestDir
#if !NETSTANDARD2_0
                , overwriteFiles: true
#endif
            );
            File.Delete(zipPath);

            if (!File.Exists(MesenExe))
            {
                Log.LogError($"Mesen executable not found after extraction: {MesenExe}");
                return false;
            }

            Log.LogMessage(MessageImportance.High, $"Mesen ready at {MesenExe}");
            return true;
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    void DownloadFile(string url, string dest)
    {
        using var response = s_http.GetAsync(url).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
        using var content = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
        using var fs = File.Create(dest);
        content.CopyTo(fs);
    }

    bool VerifySha256(string path)
    {
        using var fs = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "");
        if (!hash.Equals(ExpectedHash, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(path);
            Log.LogError($"SHA256 mismatch: expected {ExpectedHash}, got {hash}");
            return false;
        }
        Log.LogMessage(MessageImportance.Normal, $"SHA256 verified: {hash}");
        return true;
    }
}
