using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Soenneker.Clamav.Freshclam.Util.Abstract;
using Soenneker.Clamav.Runners.Definitions.Utils.Abstract;
using Soenneker.Extensions.Task;
using Soenneker.Extensions.ValueTask;
using Soenneker.Utils.Directory.Abstract;
using Soenneker.Utils.File.Abstract;
using Soenneker.Utils.Runtime;

namespace Soenneker.Clamav.Runners.Definitions.Utils;

public sealed class FileOperationsUtil : IFileOperationsUtil
{
    private readonly ILogger<FileOperationsUtil> _logger;
    private readonly IDirectoryUtil _directoryUtil;
    private readonly IFileUtil _fileUtil;
    private readonly IFreshclamUtil _freshclamUtil;

    public FileOperationsUtil(ILogger<FileOperationsUtil> logger, IDirectoryUtil directoryUtil, IFileUtil fileUtil, IFreshclamUtil freshclamUtil)
    {
        _logger = logger;
        _directoryUtil = directoryUtil;
        _fileUtil = fileUtil;
        _freshclamUtil = freshclamUtil;
    }

    public async ValueTask<string> Process(CancellationToken cancellationToken = default)
    {
        if (!RuntimeUtil.IsLinux())
            throw new PlatformNotSupportedException("The ClamAV definitions runner requires Linux x64.");

        string tempDirectory = await _directoryUtil.CreateTempDirectory(cancellationToken).NoSync();
        string databaseDirectory = Path.Combine(tempDirectory, Constants.DatabaseDirectory);
        await _directoryUtil.Create(databaseDirectory, log: false, cancellationToken).NoSync();

        string seedDirectory = Path.Combine(AppContext.BaseDirectory, "Resources", Constants.DatabaseDirectory);
        if (await _directoryUtil.Exists(seedDirectory, cancellationToken).NoSync())
        {
            _logger.LogInformation("Seeding FreshClam from packaged definitions in {SeedDirectory}", seedDirectory);
            await _fileUtil.CopyRecursively(seedDirectory, databaseDirectory, log: false, cancellationToken).NoSync();
        }
        else
        {
            _logger.LogInformation("No packaged definition seed is available; FreshClam will download the complete databases");
        }

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

}
