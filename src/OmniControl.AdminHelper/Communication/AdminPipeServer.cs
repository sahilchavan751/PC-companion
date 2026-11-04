using System.IO.Pipes;
using System.Text.Json;
using OmniControl.AdminHelper.Operations;
using OmniControl.Core.AdminContract;

namespace OmniControl.AdminHelper.Communication;

public class AdminPipeServer
{
    public const string PipeName = "OmniControl_AdminHelper_Pipe";

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine("[AdminHelper] Started elevated pipe listener.");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var pipeServer = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                Console.WriteLine("[AdminHelper] Waiting for client connection...");
                await pipeServer.WaitForConnectionAsync(linkedCts.Token);

                Console.WriteLine("[AdminHelper] Client connected.");
                using var reader = new StreamReader(pipeServer);
                using var writer = new StreamWriter(pipeServer) { AutoFlush = true };

                var jsonLine = await reader.ReadLineAsync(linkedCts.Token);
                if (!string.IsNullOrWhiteSpace(jsonLine))
                {
                    var request = JsonSerializer.Deserialize<AdminOperationRequest>(jsonLine);
                    AdminOperationResponse response;

                    if (request == null)
                    {
                        response = AdminOperationResponse.Fail(string.Empty, "Invalid empty request payload.");
                    }
                    else
                    {
                        var (isValid, errorMessage) = OperationValidator.Validate(request);
                        if (!isValid)
                        {
                            response = AdminOperationResponse.Fail(request.RequestId, $"Validation failed: {errorMessage}");
                        }
                        else
                        {
                            response = OperationExecutor.Execute(request);
                        }
                    }

                    var responseJson = JsonSerializer.Serialize(response);
                    await writer.WriteLineAsync(responseJson);
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("[AdminHelper] Pipe listener timeout reached with no requests. Exiting.");
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AdminHelper] Error in pipe loop: {ex.Message}");
            }
        }
    }
}
