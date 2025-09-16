namespace Andy.Model.Tooling;

public class ToolDeclaration
{
    /// <summary>
    /// The name of the tool
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Description of what the tool does
    /// </summary>
    public required string Description { get; set; }

    /// <summary>
    /// JSON Schema of the tool's parameters
    /// </summary>
    public Dictionary<string, object> Parameters { get; set; } = new();
}