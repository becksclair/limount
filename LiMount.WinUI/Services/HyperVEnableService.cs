using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace LiMount.WinUI.Services;

/// <summary>
/// Elevates and executes Hyper-V optional feature enablement.
/// </summary>
public sealed class HyperVEnableService : IHyperVEnableService
{
    private readonly ILogger<HyperVEnableService>? _logger;
    private readonly Func<ProcessStartInfo, Process?> _processStarter;

    public HyperVEnableService(ILogger<HyperVEnableService>? logger = null)
        : this(logger, Process.Start)
    {
    }

    internal HyperVEnableService(
        ILogger<HyperVEnableService>? logger,
        Func<ProcessStartInfo, Process?> processStarter)
    {
        _logger = logger;
        _processStarter = processStarter ?? throw new ArgumentNullException(nameof(processStarter));
    }

    public async Task<HyperVEnableResult> EnableAsync(CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dism.exe",
            Arguments = "/Online /Enable-Feature /FeatureName:Microsoft-Hyper-V /All /NoRestart",
            UseShellExecute = true,
            Verb = "runas"
        };

        try
        {
            using var process = _processStarter(startInfo);
            if (process == null)
            {
                return HyperVEnableResult.Failed("Failed to launch elevated DISM process.");
            }

            await process.WaitForExitAsync(cancellationToken);

            return FromExitCode(process.ExitCode);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED from UAC prompt.
            _logger?.LogInformation("User canceled Hyper-V elevation prompt.");
            return FromWin32Exception(ex);
        }
        catch (OperationCanceledException)
        {
            return HyperVEnableResult.Failed("Hyper-V enablement was canceled.");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to enable Hyper-V.");
            return HyperVEnableResult.Failed(ex.Message);
        }
    }

    internal static HyperVEnableResult FromExitCode(int exitCode)
    {
        return exitCode switch
        {
            0 => HyperVEnableResult.Completed(requiresRestart: false),
            3010 => HyperVEnableResult.Completed(requiresRestart: true),
            _ => HyperVEnableResult.Failed($"Hyper-V enablement command exited with code {exitCode}.")
        };
    }

    internal static HyperVEnableResult FromWin32Exception(Win32Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception.NativeErrorCode == 1223)
        {
            return HyperVEnableResult.Canceled();
        }

        return HyperVEnableResult.Failed(exception.Message);
    }
}
