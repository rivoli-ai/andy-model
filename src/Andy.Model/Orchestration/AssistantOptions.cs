namespace Andy.Model.Orchestration;

/// <summary>
/// Behavior when the bounded tool-call loop reaches its iteration limit while the
/// provider is still requesting tool calls.
/// </summary>
public enum ToolIterationLimitBehavior
{
    /// <summary>
    /// Stop the loop and return the last assistant message without executing the
    /// still-pending tool calls. The trailing assistant message may therefore
    /// contain unsatisfied tool calls; this is surfaced via
    /// <see cref="ToolIterationLimitReachedEventArgs"/>.
    /// </summary>
    StopWithoutExecuting,

    /// <summary>
    /// Throw <see cref="ToolIterationLimitException"/> when the limit is exceeded.
    /// </summary>
    Throw
}

/// <summary>
/// Options controlling orchestration behavior shared by <see cref="Assistant"/> and
/// <see cref="AssistantWithManager"/>.
/// </summary>
public sealed class AssistantOptions
{
    /// <summary>
    /// Maximum number of tool-execution rounds within a single turn. After this many
    /// rounds of executing tools, if the provider still returns tool calls the loop
    /// stops per <see cref="OnToolIterationLimit"/>. Must be at least 1.
    /// A value of 1 reproduces the classic single-round behavior.
    /// </summary>
    public int MaxToolIterations { get; init; } = 8;

    /// <summary>
    /// What to do when <see cref="MaxToolIterations"/> is reached with tool calls
    /// still pending. Defaults to stopping without executing the pending calls.
    /// </summary>
    public ToolIterationLimitBehavior OnToolIterationLimit { get; init; } = ToolIterationLimitBehavior.StopWithoutExecuting;

    /// <summary>
    /// When true, tool-error result payloads include the underlying exception type
    /// and message. Defaults to false so that sensitive exception details are not
    /// exposed to the model or persisted history by default. A stable error code and
    /// the tool name are always included.
    /// </summary>
    public bool IncludeExceptionDetailsInToolErrors { get; init; }

    /// <summary>Shared default instance.</summary>
    public static AssistantOptions Default { get; } = new();

    internal void Validate()
    {
        if (MaxToolIterations < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxToolIterations), MaxToolIterations,
                "MaxToolIterations must be at least 1.");
        }
    }
}

/// <summary>
/// Thrown when the tool-call loop exceeds <see cref="AssistantOptions.MaxToolIterations"/>
/// and <see cref="AssistantOptions.OnToolIterationLimit"/> is
/// <see cref="ToolIterationLimitBehavior.Throw"/>.
/// </summary>
public sealed class ToolIterationLimitException : Exception
{
    public int MaxToolIterations { get; }

    public ToolIterationLimitException(int maxToolIterations)
        : base($"Tool-call loop exceeded the configured maximum of {maxToolIterations} iterations.")
    {
        MaxToolIterations = maxToolIterations;
    }
}
