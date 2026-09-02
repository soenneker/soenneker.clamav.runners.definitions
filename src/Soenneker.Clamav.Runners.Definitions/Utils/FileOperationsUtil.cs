using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Soenneker.Clamav.Freshclam.Util.Abstract;
using Soenneker.Clamav.Runners.Definitions.Utils.Abstract;
using Soenneker.Extensions.Task;
using Soenneker.Extensions.ValueTask;
using Soenneker.NuGet.Client.Abstract;
using Soenneker.Utils.Directory.Abstract;
using Soenneker.Utils.File.Abstract;
using Soenneker.Utils.NuGet.Abstract;
using Soenneker.Utils.PooledStringBuilders;
using Soenneker.Utils.Runtime;

namespace Soenneker.Clamav.Runners.Definitions.Utils;

public sealed class FileOperationsUtil : IFileOperationsUtil
{
    private readonly ILogger<FileOperationsUtil> _logger;
    private readonly IDirectoryUtil _directoryUtil;
    private readonly IFileUtil _fileUtil;
    private readonly IFreshclamUtil _freshclamUtil;
    private readonly INuGetUtil _nuGetUtil;
    private readonly INuGetClient _nuGetClient;

    public FileOperationsUtil(ILogger<FileOperationsUtil> logger, IDirectoryUtil directoryUtil, IFileUtil fileUtil, IFreshclamUtil freshclamUtil,
        INuGetUtil nuGetUtil, INuGetClient nuGetClient)
    {
        _logger = logger;
        _directoryUtil = directoryUtil;
        _fileUtil = fileUtil;
        _freshclamUtil = freshclamUtil;
        _nuGetUtil = nuGetUtil;
        _nuGetClient = nuGetClient;
    }

    public async ValueTask<string> Process(CancellationToken cancellationToken = default)
    {
        if (!RuntimeUtil.IsLinux())
            throw new PlatformNotSupportedException("The ClamAV definitions runner requires Linux x64.");

        string tempDirectory = await _directoryUtil.CreateTempDirectory(cancellationToken).NoSync();
        string databaseDirectory = Path.Combine(tempDirectory, Constants.DatabaseDirectory);
        await _directoryUtil.Create(databaseDirectory, log: false, cancellationToken).NoSync();

        await TrySeedFromLatestPackage(databaseDirectory, tempDirectory, cancellationToken).NoSync();

        string runtimeDirectory = Path.Combine(AppContext.BaseDirectory, "Resources", "linux-x64", "freshclam");
        string copyingPath = Path.Combine(runtimeDirectory, "COPYING.txt");

        _logger.LogInformation("Updating ClamAV definition seed in {DatabaseDirectory}", databaseDirectory);
        var output = await _freshclamUtil.Update(databaseDirectory, cancellationToken: cancellationToken).NoSync();
        _logger.LogInformation("FreshClam completed with {OutputLineCount} output lines", output.Count);

        if (await _fileUtil.Exists(copyingPath, cancellationToken).NoSync())
            await _fileUtil.Copy(copyingPath, Path.Combine(databaseDirectory, "COPYING.txt"), log: false, cancellationToken).NoSync();

        const string source = "Official ClamAV virus databases\n" +
                              "Database service: https://database.clamav.net/\n" +
                              "Database documentation: https://docs.clamav.net/faq/faq-cvd.html\n" +
                              "Updated with the official FreshClam client.\n";
        await _fileUtil.Write(Path.Combine(databaseDirectory, "SOURCE.txt"), source, log: false, cancellationToken).NoSync();

        return databaseDirectory;
    }

    private async ValueTask TrySeedFromLatestPackage(string databaseDirectory, string tempDirectory, CancellationToken cancellationToken)
    {
        try
        {
            string? version = await _nuGetUtil.GetLatestListedVersion(Constants.Library, cancellationToken: cancellationToken).NoSync();

            if (version == null)
            {
                _logger.LogInformation("No published {PackageId} package is available; FreshClam will download the complete databases", Constants.Library);
                return;
            }

            string packageBaseAddress = await _nuGetUtil.GetServiceUri("PackageBaseAddress/3.0.0", cancellationToken: cancellationToken).NoSync();
            string packageId = Constants.Library.ToLowerInvariant();
            string normalizedVersion = version.ToLowerInvariant();
            string packageUri = BuildPackageUri(packageBaseAddress, packageId, normalizedVersion);
            string packagePath = Path.Combine(tempDirectory, $"{packageId}.{normalizedVersion}.nupkg");

            _logger.LogInformation("Downloading {PackageId} {Version} to seed FreshClam", Constants.Library, version);

            HttpClient client = await _nuGetClient.Get(cancellationToken).NoSync();
            using HttpResponseMessage response = await client.GetAsync(packageUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).NoSync();
            response.EnsureSuccessStatusCode();

            await using (Stream packageStream = await response.Content.ReadAsStreamAsync(cancellationToken).NoSync())
                await _fileUtil.Write(packagePath, packageStream, log: false, cancellationToken).NoSync();

            const string prefix = "contentFiles/any/any/Resources/clamav-database/";
            var extractedFiles = 0;

            await using FileStream fileStream = _fileUtil.OpenRead(packagePath, log: false);
            using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read);

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (!entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || entry.FullName.EndsWith('/'))
                    continue;

                string relativePath = entry.FullName[prefix.Length..];
                string destinationPath = Path.GetFullPath(Path.Combine(databaseDirectory, relativePath));
                string databaseRoot = Path.GetFullPath(databaseDirectory) + Path.DirectorySeparatorChar;

                if (!destinationPath.StartsWith(databaseRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Package entry escapes the definitions directory: {entry.FullName}");

                string? destinationDirectory = Path.GetDirectoryName(destinationPath);
                if (destinationDirectory != null)
                    await _directoryUtil.Create(destinationDirectory, log: false, cancellationToken).NoSync();

                await using Stream entryStream = entry.Open();
                await _fileUtil.Write(destinationPath, entryStream, log: false, cancellationToken).NoSync();
                extractedFiles++;
            }

            if (extractedFiles == 0)
                throw new InvalidDataException($"{Constants.Library} {version} did not contain a ClamAV database seed");

            _logger.LogInformation("Seeded FreshClam with {FileCount} files from {PackageId} {Version}", extractedFiles, Constants.Library, version);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception,
                "Could not seed FreshClam from the latest {PackageId} package; FreshClam will download the complete databases", Constants.Library);
        }
    }

    private static string BuildPackageUri(string packageBaseAddress, string packageId, string version)
    {
        using var builder = new PooledStringBuilder(packageBaseAddress.Length + (packageId.Length * 2) + (version.Length * 2) + 10);
        builder.Append(packageBaseAddress.TrimEnd('/'));
        builder.Append('/');
        builder.Append(packageId);
        builder.Append('/');
        builder.Append(version);
        builder.Append('/');
        builder.Append(packageId);
        builder.Append('.');
        builder.Append(version);
        builder.Append(".nupkg");
        return builder.ToString();
    }

}
