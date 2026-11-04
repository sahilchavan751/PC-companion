using System.Security.Principal;
using OmniControl.AdminHelper.Communication;

namespace OmniControl.AdminHelper;

internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.WriteLine("=== OmniControl Privileged Helper ===");

        if (!IsAdministrator())
        {
            Console.Error.WriteLine("[FATAL] OmniControl.AdminHelper must be executed with Administrator privileges.");
            return 5; // ERROR_ACCESS_DENIED
        }

        Console.WriteLine("[INFO] Elevated privileges confirmed.");

        try
        {
            var server = new AdminPipeServer();
            using var cts = new CancellationTokenSource();

            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            await server.RunAsync(cts.Token);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[FATAL] Error running helper: {ex.Message}");
            return 1;
        }
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
