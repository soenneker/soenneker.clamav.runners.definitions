using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Soenneker.Clamav.Runners.Definitions.Utils.Abstract;
using Soenneker.Extensions.Task;
using Soenneker.Extensions.ValueTask;
using Soenneker.Utils.Directory.Abstract;
using Soenneker.Utils.File.Abstract;
using Soenneker.Utils.PooledStringBuilders;
using Soenneker.Utils.Process.Abstract;
using Soenneker.Utils.Runtime;

namespace Soenneker.Clamav.Runners.Definitions.Utils;

public sealed class FileOperationsUtil : IFileOperationsUtil
{
    private static readonly string[] _requiredDatabases = ["main", "daily", "bytecode"];

    private readonly ILogger<FileOperationsUtil> _logger;
    private readonly IDirectoryUtil _directoryUtil;
    private readonly IFileUtil _fileUtil;
    private readonly IProcessUtil _processUtil;

    public FileOperationsUtil(ILogger<FileOperationsUtil> logger, IDirectoryUtil directoryUtil, IFileUtil fileUtil, IProcessUtil processUtil)
    {
        _logger = logger;
        _directoryUtil = directoryUtil;
        _fileUtil = fileUtil;
        _processUtil = processUtil;
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

        string runtimeDirectory = Path.Combine(AppContext.BaseDirectory, "Resources", "linux-x64", "clamav");
        string binDirectory = Path.Combine(runtimeDirectory, "bin");
        string freshclamPath = Path.Combine(binDirectory, "freshclam");
        string certificatesDirectory = Path.Combine(runtimeDirectory, "etc", "certs");
        string copyingPath = Path.Combine(runtimeDirectory, "COPYING.txt");

        if (!await _fileUtil.Exists(freshclamPath, cancellationToken).NoSync())
            throw new FileNotFoundException("The bundled freshclam executable was not found.", freshclamPath);

        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(freshclamPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        string configurationPath = Path.Combine(tempDirectory, "freshclam.conf");
        await _fileUtil.Write(configurationPath, BuildConfiguration(), log: false, cancellationToken).NoSync();

        var environmentVariables = new Dictionary<string, string>
        {
            ["LD_LIBRARY_PATH"] = $"{Path.Combine(runtimeDirectory, "lib64")}{Path.PathSeparator}{Path.Combine(runtimeDirectory, "lib")}",
            ["CVD_CERTS_DIR"] = certificatesDirectory
        };

        string arguments = $"--config-file={Quote(configurationPath)} --datadir={Quote(databaseDirectory)} " +
                           $"--cvdcertsdir={Quote(certificatesDirectory)} --stdout";

        _logger.LogInformation("Updating ClamAV definition seed in {DatabaseDirectory}", databaseDirectory);
        List<string> output = await _processUtil.Start(freshclamPath, runtimeDirectory, arguments, log: false,
            environmentalVars: environmentVariables, cancellationToken: cancellationToken).NoSync();
        _logger.LogInformation("FreshClam completed with {OutputLineCount} output lines", output.Count);

        await ValidateDatabases(databaseDirectory, cancellationToken).NoSync();
        await _fileUtil.TryDelete(Path.Combine(databaseDirectory, "freshclam.dat"), log: false, cancellationToken).NoSync();

        if (await _fileUtil.Exists(copyingPath, cancellationToken).NoSync())
            await _fileUtil.Copy(copyingPath, Path.Combine(databaseDirectory, "COPYING.txt"), log: false, cancellationToken).NoSync();

        const string source = "Official ClamAV virus databases\n" +
                              "Database service: https://database.clamav.net/\n" +
                              "Database documentation: https://docs.clamav.net/faq/faq-cvd.html\n" +
                              "Updated with the official FreshClam client.\n";
        await _fileUtil.Write(Path.Combine(databaseDirectory, "SOURCE.txt"), source, log: false, cancellationToken).NoSync();

        return databaseDirectory;
    }

    private async ValueTask ValidateDatabases(string databaseDirectory, CancellationToken cancellationToken)
    {
        foreach (string database in _requiredDatabases)
        {
            string cvdPath = Path.Combine(databaseDirectory, $"{database}.cvd");
            string cldPath = Path.Combine(databaseDirectory, $"{database}.cld");
            if (!await _fileUtil.Exists(cvdPath, cancellationToken).NoSync() &&
                !await _fileUtil.Exists(cldPath, cancellationToken).NoSync())
                throw new InvalidDataException($"FreshClam did not produce the required {database} database.");
        }

        _logger.LogInformation("Validated the main, daily, and bytecode ClamAV databases");
    }

    private static string BuildConfiguration()
    {
        using var builder = new PooledStringBuilder(192);
        builder.AppendLine("DatabaseMirror database.clamav.net");
        builder.AppendLine("ScriptedUpdates yes");
        builder.AppendLine("CompressLocalDatabase no");
        builder.AppendLine("Checks 12");
        builder.Append("DatabaseOwner ");
        builder.AppendLine(Environment.UserName);
        return builder.ToString();
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
}
