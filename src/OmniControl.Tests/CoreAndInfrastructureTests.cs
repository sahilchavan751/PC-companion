using OmniControl.Core.AdminContract;
using OmniControl.Core.Models.Environment;
using OmniControl.Core.Models.Processes;
using OmniControl.Core.Results;
using Xunit;

namespace OmniControl.Tests;

public class CoreAndInfrastructureTests
{
    [Fact]
    public void OperationValidator_AllowsValidSystemEnvironmentVariable()
    {
        var request = new AdminOperationRequest
        {
            OperationType = AdminOperationType.SetMachineEnvironmentVariable,
            Parameters =
            {
                ["Name"] = "MY_CUSTOM_VAR",
                ["Value"] = "C:\\Tools"
            }
        };

        var (isValid, errorMessage) = OperationValidator.Validate(request);

        Assert.True(isValid);
        Assert.Null(errorMessage);
    }

    [Theory]
    [InlineData("VAR;rmdir /s /q C:\\")]
    [InlineData("VAR&calc.exe")]
    [InlineData("VAR|powershell")]
    [InlineData("")]
    public void OperationValidator_RejectsMaliciousEnvironmentVariableNames(string maliciousName)
    {
        var request = new AdminOperationRequest
        {
            OperationType = AdminOperationType.SetMachineEnvironmentVariable,
            Parameters =
            {
                ["Name"] = maliciousName,
                ["Value"] = "Test"
            }
        };

        var (isValid, errorMessage) = OperationValidator.Validate(request);

        Assert.False(isValid);
        Assert.NotNull(errorMessage);
    }

    [Fact]
    public void OperationValidator_BlocksStoppingCoreSystemServices()
    {
        var request = new AdminOperationRequest
        {
            OperationType = AdminOperationType.ControlService,
            Parameters =
            {
                ["ServiceName"] = "RpcSs",
                ["Action"] = "Stop"
            }
        };

        var (isValid, errorMessage) = OperationValidator.Validate(request);

        Assert.False(isValid);
        Assert.Contains("stability", errorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OperationValidator_AllowsValidServiceRestart()
    {
        var request = new AdminOperationRequest
        {
            OperationType = AdminOperationType.ControlService,
            Parameters =
            {
                ["ServiceName"] = "Spooler",
                ["Action"] = "Restart"
            }
        };

        var (isValid, errorMessage) = OperationValidator.Validate(request);

        Assert.True(isValid);
        Assert.Null(errorMessage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void OperationValidator_BlocksTerminatingKernelSystemPids(int criticalPid)
    {
        var request = new AdminOperationRequest
        {
            OperationType = AdminOperationType.TerminateElevatedProcess,
            Parameters =
            {
                ["ProcessId"] = criticalPid.ToString()
            }
        };

        var (isValid, errorMessage) = OperationValidator.Validate(request);

        Assert.False(isValid);
        Assert.Contains("kernel", errorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OperationValidator_AllowsTerminatingStandardProcess()
    {
        var request = new AdminOperationRequest
        {
            OperationType = AdminOperationType.TerminateElevatedProcess,
            Parameters =
            {
                ["ProcessId"] = "12345"
            }
        };

        var (isValid, errorMessage) = OperationValidator.Validate(request);

        Assert.True(isValid);
        Assert.Null(errorMessage);
    }

    [Fact]
    public void OperationResult_ConstructsCorrectStatusAndMessages()
    {
        var ok = OperationResult.Ok("Custom Success");
        var fail = OperationResult.Fail("Custom Error", "Details");
        var elev = OperationResult.RequiresElevated();

        Assert.True(ok.IsSuccess);
        Assert.Equal(OperationStatus.Success, ok.Status);
        Assert.False(fail.IsSuccess);
        Assert.Equal(OperationStatus.Failed, fail.Status);
        Assert.Equal(OperationStatus.RequiresElevation, elev.Status);
    }

    [Fact]
    public void PathItem_DetectsDuplicatesProperly()
    {
        var rawPath = "C:\\Windows;C:\\Windows\\System32;C:\\Windows";
        var parts = rawPath.Split(';', StringSplitOptions.RemoveEmptyEntries);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<PathItem>();

        for (int i = 0; i < parts.Length; i++)
        {
            var p = parts[i];
            items.Add(new PathItem
            {
                Index = i + 1,
                Path = p,
                IsDuplicate = !seen.Add(p)
            });
        }

        Assert.False(items[0].IsDuplicate);
        Assert.False(items[1].IsDuplicate);
        Assert.True(items[2].IsDuplicate);
    }
}
