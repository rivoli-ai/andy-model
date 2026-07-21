using System.Text.Json;
using Andy.Model.Model;
using Andy.Model.Utils;

namespace Andy.Model.Tooling;

/// <summary>
/// Validates a tool call's arguments against the JSON Schema declared in
/// <see cref="ToolDeclaration.Parameters"/>.
/// </summary>
/// <remarks>
/// Validation order: the arguments must be present and parse as JSON, then they are
/// checked against the declared schema using <see cref="JsonSchemaValidator"/> (see that
/// type for the supported vocabulary and how unsupported keywords are handled). A tool
/// whose <see cref="ToolDeclaration.Parameters"/> is empty accepts any well-formed JSON.
/// Validation failures are returned in <see cref="ValidationResult.Errors"/> with stable
/// JSON-Pointer paths; callers must not invoke a tool when
/// <see cref="ValidationResult.IsValid"/> is false.
/// </remarks>
public static class ToolCallValidator
{
    public static ValidationResult Validate(ToolCall call, ToolDeclaration definition)
    {
        var result = new ValidationResult { IsValid = true };

        // 1) Arguments must be present.
        if (string.IsNullOrWhiteSpace(call.ArgumentsJson))
        {
            result.Errors.Add("Arguments cannot be empty");
            result.IsValid = false;
            return result;
        }

        // 2) Arguments must parse as JSON.
        JsonElement args;
        try
        {
            args = call.ArgumentsAsJsonElement();
        }
        catch (JsonException ex)
        {
            result.Errors.Add($"Invalid JSON arguments: {ex.Message}");
            result.IsValid = false;
            return result;
        }

        // 3) If a schema is declared, validate against it.
        if (definition.Parameters == null || definition.Parameters.Count == 0)
        {
            return result; // No schema: any well-formed JSON is accepted.
        }

        JsonElement schema;
        try
        {
            var schemaJson = JsonSerializer.Serialize(definition.Parameters, JsonOptions.Default);
            using var schemaDoc = JsonDocument.Parse(schemaJson);
            schema = schemaDoc.RootElement.Clone();
        }
        catch (JsonException)
        {
            // Schema itself is not serializable/parseable: accept rather than block the tool.
            return result;
        }

        var errors = new List<string>();
        JsonSchemaValidator.Validate(args, schema, string.Empty, errors);
        if (errors.Count > 0)
        {
            result.Errors.AddRange(errors);
            result.IsValid = false;
        }

        return result;
    }
}
