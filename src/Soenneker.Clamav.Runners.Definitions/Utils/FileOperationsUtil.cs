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
        string tempDirectory = await _directoryUtil.CreateTempDirectory(cancellationToken).NoSync();
        string databaseDirectory = Path.Combine(tempDirectory, Constants.DatabaseDirectory);
        await _directoryUtil.Create(databaseDirectory, log: false, cancellationToken).NoSync();

        _logger.LogInformation("Downloading current ClamAV definitions into {DatabaseDirectory}", databaseDirectory);
        var output = await _freshclamUtil.Update(databaseDirectory, cancellationToken: cancellationToken).NoSync();
        _logger.LogInformation("FreshClam completed with {OutputLineCount} output lines", output.Count);

        string runtimeIdentifier = OperatingSystem.IsWindows() ? "win-x64" : "linux-x64";
        string copyingPath = Path.Combine(AppContext.BaseDirectory, "Resources", runtimeIdentifier, "freshclam", "COPYING.txt");

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
