using System.Diagnostics;

namespace DataMart.TestSupport;

public static class AspireTestSupport
{
    public static async Task ResetPersistentDatabaseAsync()
    {
        try
        {
            var list = Process.Start(new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "ps -a --filter \"name=dbserver-\" --format \"{{.Names}}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (list is null) return;
            var stdout = await list.StandardOutput.ReadToEndAsync();
            await list.WaitForExitAsync();

            foreach (var name in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = name.Trim();
                if (trimmed.Length == 0 || !trimmed.StartsWith("dbserver-", StringComparison.Ordinal))
                {
                    continue;
                }

                var remove = Process.Start(new ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = $"rm -f {trimmed}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                if (remove is not null)
                {
                    await remove.WaitForExitAsync();
                }
            }
        }
        catch
        {
        }
    }
}