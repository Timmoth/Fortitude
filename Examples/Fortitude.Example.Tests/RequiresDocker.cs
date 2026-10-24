using System.Diagnostics;
using Xunit;

namespace Fortitude.Example.Api;

/// <summary>
///     A <see cref="FactAttribute" /> that skips when there is no usable Docker daemon.
/// </summary>
/// <remarks>
///     These tests build an image and run a container. Without this they fail with a
///     connection error that looks like a Fortitude problem rather than a missing
///     prerequisite, which makes a first `dotnet test` on a fresh machine misleading.
/// </remarks>
public sealed class RequiresDockerFactAttribute : FactAttribute
{
    public RequiresDockerFactAttribute()
    {
        if (!DockerEnvironment.IsAvailable)
            Skip = "Docker is not available on this machine, so the container tests were skipped.";
    }
}

/// <summary>Detects whether Docker can be used, once per process.</summary>
public static class DockerEnvironment
{
    private static readonly Lazy<bool> Available = new(Probe, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>True when a Docker daemon responds.</summary>
    public static bool IsAvailable => Available.Value;

    private static bool Probe()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("docker", "info")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });

            if (process == null) return false;

            // `docker info` hangs rather than failing when the daemon is unreachable in some
            // configurations, so do not wait indefinitely for an answer.
            if (!process.WaitForExit(TimeSpan.FromSeconds(10)))
            {
                try { process.Kill(true); } catch { /* best effort */ }
                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            // docker not on PATH at all.
            return false;
        }
    }
}
