using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using OmniControl.Core.AdminContract;
using OmniControl.Core.Results;

namespace OmniControl.Infrastructure.Admin;

public class AdminElevationService : IAdminElevationService
{
    private const string PipeName = "OmniControl_AdminHelper_Pipe";
    private readonly SemaphoreSlim _lock = new(1, 1);

    public bool IsAppRunningElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public async Task<bool> EnsureHelperElevatedAsync(CancellationToken cancellationToken = default)
    {
        if (IsAppRunningElevated)
            return true;

        try
        {
            // Try connecting to existing helper first
            using var testPipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
            await testPipe.ConnectAsync(500, cancellationToken);
            return true;
        }
        catch
        {
            // Helper not running, need to launch with "runas"
            return LaunchElevatedHelper();
        }
    }

    public async Task<OperationResult> ExecuteElevatedAsync(AdminOperationRequest request, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            // If already elevated in the main process, we can execute directly via helper logic
            if (IsAppRunningElevated)
            {
                var (isValid, errorMessage) = OperationValidator.Validate(request);
                if (!isValid)
                {
                    return OperationResult.Fail($"Admin operation validation failed: {errorMessage}");
                }
            }

            // Ensure elevated helper process is running
            bool connected = false;
            using var pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);

            try
            {
                await pipeClient.ConnectAsync(1000, cancellationToken);
                connected = true;
            }
            catch
            {
                // Not running, launch helper with UAC prompt
                if (!LaunchElevatedHelper())
                {
                    return OperationResult.RequiresElevated("Administrator approval was declined or cancelled.");
                }

                // Wait for helper to initialize pipe
                for (int i = 0; i < 15; i++)
                {
                    try
                    {
                        await Task.Delay(300, cancellationToken);
                        await pipeClient.ConnectAsync(500, cancellationToken);
                        connected = true;
                        break;
                    }
                    catch
                    {
                        // Retry until helper pipe is available
                    }
                }
            }

            if (!connected)
            {
                return OperationResult.Fail("Failed to connect to the elevated helper service.");
            }

            using var writer = new StreamWriter(pipeClient) { AutoFlush = true };
            using var reader = new StreamReader(pipeClient);

            var requestJson = JsonSerializer.Serialize(request);
            await writer.WriteLineAsync(requestJson);

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var responseJson = await reader.ReadLineAsync(linkedCts.Token);
            if (string.IsNullOrWhiteSpace(responseJson))
            {
                return OperationResult.Fail("Received empty response from elevated helper.");
            }

            var response = JsonSerializer.Deserialize<AdminOperationResponse>(responseJson);
            return response != null ? response.ToOperationResult() : OperationResult.Fail("Failed to parse helper response.");
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Cancel("Elevated operation request timed out or was cancelled.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"Elevated execution failed: {ex.Message}", ex.ToString());
        }
        finally
        {
            _lock.Release();
        }
    }

    private static bool LaunchElevatedHelper()
    {
        try
        {
            var appDir = AppContext.BaseDirectory;
            var helperExe = Path.Combine(appDir, "OmniControl.AdminHelper.exe");

            if (!File.Exists(helperExe))
            {
                string[] candidates =
                {
                    Path.GetFullPath(Path.Combine(appDir, @"..\..\..\..\OmniControl.AdminHelper\bin\Release\net10.0-windows10.0.26100.0\OmniControl.AdminHelper.exe")),
                    Path.GetFullPath(Path.Combine(appDir, @"..\..\..\..\OmniControl.AdminHelper\bin\Debug\net10.0-windows10.0.26100.0\OmniControl.AdminHelper.exe")),
                    Path.GetFullPath(Path.Combine(appDir, @"..\..\..\..\..\OmniControl.AdminHelper\bin\Release\net10.0-windows10.0.26100.0\OmniControl.AdminHelper.exe")),
                    Path.GetFullPath(Path.Combine(appDir, @"..\..\..\..\..\OmniControl.AdminHelper\bin\Debug\net10.0-windows10.0.26100.0\OmniControl.AdminHelper.exe")),
                    @"C:\pc-control-app\src\OmniControl.AdminHelper\bin\Release\net10.0-windows10.0.26100.0\OmniControl.AdminHelper.exe",
                    @"C:\pc-control-app\src\OmniControl.AdminHelper\bin\Debug\net10.0-windows10.0.26100.0\OmniControl.AdminHelper.exe"
                };

                foreach (var cand in candidates)
                {
                    if (File.Exists(cand))
                    {
                        helperExe = cand;
                        break;
                    }
                }
            }

            if (!File.Exists(helperExe))
            {
                return false;
            }

            var psi = new ProcessStartInfo
            {
                FileName = helperExe,
                WorkingDirectory = Path.GetDirectoryName(helperExe),
                UseShellExecute = true,
                Verb = "runas" // Windows UAC Elevation prompt
            };

            var proc = Process.Start(psi);
            return proc != null;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
