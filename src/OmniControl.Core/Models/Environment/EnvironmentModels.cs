namespace OmniControl.Core.Models.Environment;

public enum EnvironmentVariableScope
{
    User,
    Machine
}

public class EnvVariableItem
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public EnvironmentVariableScope Scope { get; set; }
    public bool IsPath => string.Equals(Name, "PATH", StringComparison.OrdinalIgnoreCase);
    public int EntryCount => IsPath ? Value.Split(';', StringSplitOptions.RemoveEmptyEntries).Length : 1;
}

public class PathItem
{
    public int Index { get; set; }
    public string Path { get; set; } = string.Empty;
    public EnvironmentVariableScope Scope { get; set; }
    public bool Exists { get; set; }
    public bool IsDuplicate { get; set; }
    public string StatusText => !Exists ? "Path does not exist" : (IsDuplicate ? "Duplicate entry" : "Valid");
}
