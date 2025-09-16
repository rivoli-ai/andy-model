// SPDX-License-Identifier: MIT
// LLM-Agnostic Conversation Framework in C# (No external dependencies)
// Target: .NET 8+ (uses only BCL)

namespace Andy.Model.Model;

#region Core Message Model

/// <summary>
/// Roles roughly compatible with common chat schemas.
/// </summary>
public enum Role
{
    System,
    User,
    Assistant,
    Tool
}

#endregion