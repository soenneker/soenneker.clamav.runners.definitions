using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Clamav.Runners.Definitions.Utils.Abstract;

public interface IFileOperationsUtil
{
    /// <summary>
    /// Produces a validated, current ClamAV definition directory suitable for packaging.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The temporary definition directory.</returns>
    ValueTask<string> Process(CancellationToken cancellationToken = default);
}
