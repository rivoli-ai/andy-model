namespace Andy.Model.Tooling;

/// <summary>
/// Tool call validation result.
/// </summary>
public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; init; } = new();
}
