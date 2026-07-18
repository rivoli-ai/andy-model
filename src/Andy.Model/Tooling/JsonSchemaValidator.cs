using System.Globalization;
using System.Text.Json;

namespace Andy.Model.Tooling;

/// <summary>
/// A focused JSON Schema validator implementing the subset of Draft-07 vocabulary
/// that tool declarations commonly use. It is deliberately dependency-free.
/// </summary>
/// <remarks>
/// <para><b>Supported keywords:</b>
/// <c>type</c> (object, array, string, number, integer, boolean, null; or an array of
/// those), <c>properties</c>, <c>required</c>, <c>additionalProperties</c> (boolean or
/// subschema), <c>items</c> (single subschema), <c>enum</c>, <c>const</c>,
/// <c>minimum</c>, <c>maximum</c>, <c>minLength</c>, <c>maxLength</c>, <c>minItems</c>,
/// <c>maxItems</c>.</para>
/// <para><b>Unsupported keywords</b> (for example <c>$ref</c>, <c>allOf</c>/<c>anyOf</c>/
/// <c>oneOf</c>, <c>pattern</c>, tuple-form <c>items</c>, <c>patternProperties</c>) are
/// ignored rather than rejected, so a schema using them still validates the keywords
/// that are understood.</para>
/// <para>Error paths use JSON-Pointer style (<c>/filters/date_range/start</c>), with the
/// root shown as <c>(root)</c>.</para>
/// </remarks>
internal static class JsonSchemaValidator
{
    public static void Validate(JsonElement instance, JsonElement schema, string path, List<string> errors)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            return; // Not a schema object we can interpret.
        }

        ValidateType(instance, schema, path, errors);
        ValidateEnum(instance, schema, path, errors);
        ValidateConst(instance, schema, path, errors);
        ValidateNumericRange(instance, schema, path, errors);
        ValidateStringLength(instance, schema, path, errors);

        if (instance.ValueKind == JsonValueKind.Object)
        {
            ValidateObject(instance, schema, path, errors);
        }
        else if (instance.ValueKind == JsonValueKind.Array)
        {
            ValidateArray(instance, schema, path, errors);
        }
    }

    private static void ValidateType(JsonElement instance, JsonElement schema, string path, List<string> errors)
    {
        if (!schema.TryGetProperty("type", out var typeElement))
        {
            return;
        }

        var allowed = new List<string>();
        if (typeElement.ValueKind == JsonValueKind.String)
        {
            allowed.Add(typeElement.GetString()!);
        }
        else if (typeElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in typeElement.EnumerateArray())
            {
                if (t.ValueKind == JsonValueKind.String) allowed.Add(t.GetString()!);
            }
        }

        if (allowed.Count == 0)
        {
            return;
        }

        if (!allowed.Any(t => MatchesType(instance, t)))
        {
            errors.Add($"{Display(path)}: expected type {string.Join(" or ", allowed)} but got {TypeName(instance)}.");
        }
    }

    private static bool MatchesType(JsonElement instance, string type) => type switch
    {
        "object" => instance.ValueKind == JsonValueKind.Object,
        "array" => instance.ValueKind == JsonValueKind.Array,
        "string" => instance.ValueKind == JsonValueKind.String,
        "boolean" => instance.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "null" => instance.ValueKind == JsonValueKind.Null,
        "number" => instance.ValueKind == JsonValueKind.Number,
        "integer" => instance.ValueKind == JsonValueKind.Number && IsInteger(instance),
        _ => true // unknown type keyword: don't fail
    };

    private static bool IsInteger(JsonElement number)
        => number.TryGetInt64(out _) ||
           (number.TryGetDouble(out var d) && Math.Floor(d) == d && !double.IsInfinity(d));

    private static void ValidateEnum(JsonElement instance, JsonElement schema, string path, List<string> errors)
    {
        if (!schema.TryGetProperty("enum", out var enumElement) || enumElement.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var options = enumElement.EnumerateArray().ToList();
        if (options.Count > 0 && !options.Any(o => JsonEquals(o, instance)))
        {
            var rendered = string.Join(", ", options.Select(o => o.GetRawText()));
            errors.Add($"{Display(path)}: value {instance.GetRawText()} is not one of the allowed values [{rendered}].");
        }
    }

    private static void ValidateConst(JsonElement instance, JsonElement schema, string path, List<string> errors)
    {
        if (schema.TryGetProperty("const", out var constElement) && !JsonEquals(constElement, instance))
        {
            errors.Add($"{Display(path)}: value must equal {constElement.GetRawText()}.");
        }
    }

    private static void ValidateNumericRange(JsonElement instance, JsonElement schema, string path, List<string> errors)
    {
        if (instance.ValueKind != JsonValueKind.Number || !instance.TryGetDouble(out var value))
        {
            return;
        }

        if (schema.TryGetProperty("minimum", out var min) && min.ValueKind == JsonValueKind.Number
            && min.TryGetDouble(out var minVal) && value < minVal)
        {
            errors.Add($"{Display(path)}: value {value.ToString(CultureInfo.InvariantCulture)} is less than minimum {minVal.ToString(CultureInfo.InvariantCulture)}.");
        }

        if (schema.TryGetProperty("maximum", out var max) && max.ValueKind == JsonValueKind.Number
            && max.TryGetDouble(out var maxVal) && value > maxVal)
        {
            errors.Add($"{Display(path)}: value {value.ToString(CultureInfo.InvariantCulture)} is greater than maximum {maxVal.ToString(CultureInfo.InvariantCulture)}.");
        }
    }

    private static void ValidateStringLength(JsonElement instance, JsonElement schema, string path, List<string> errors)
    {
        if (instance.ValueKind != JsonValueKind.String)
        {
            return;
        }

        var length = instance.GetString()!.Length;
        if (schema.TryGetProperty("minLength", out var minLen) && minLen.TryGetInt32(out var min) && length < min)
        {
            errors.Add($"{Display(path)}: string length {length} is less than minLength {min}.");
        }
        if (schema.TryGetProperty("maxLength", out var maxLen) && maxLen.TryGetInt32(out var max) && length > max)
        {
            errors.Add($"{Display(path)}: string length {length} is greater than maxLength {max}.");
        }
    }

    private static void ValidateObject(JsonElement instance, JsonElement schema, string path, List<string> errors)
    {
        var propertySchemas = schema.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object
            ? props
            : default;

        // required
        if (schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
        {
            foreach (var req in required.EnumerateArray())
            {
                if (req.ValueKind == JsonValueKind.String)
                {
                    var name = req.GetString()!;
                    if (!instance.TryGetProperty(name, out _))
                    {
                        errors.Add($"{Display(path)}: missing required property '{name}'.");
                    }
                }
            }
        }

        // additionalProperties
        JsonElement additionalSchema = default;
        var additionalAllowed = true;
        var additionalIsSchema = false;
        if (schema.TryGetProperty("additionalProperties", out var addl))
        {
            if (addl.ValueKind == JsonValueKind.False) additionalAllowed = false;
            else if (addl.ValueKind == JsonValueKind.Object) { additionalIsSchema = true; additionalSchema = addl; }
        }

        foreach (var member in instance.EnumerateObject())
        {
            var childPath = $"{path}/{member.Name}";
            if (propertySchemas.ValueKind == JsonValueKind.Object && propertySchemas.TryGetProperty(member.Name, out var childSchema))
            {
                Validate(member.Value, childSchema, childPath, errors);
            }
            else if (!additionalAllowed)
            {
                errors.Add($"{Display(path)}: additional property '{member.Name}' is not permitted.");
            }
            else if (additionalIsSchema)
            {
                Validate(member.Value, additionalSchema, childPath, errors);
            }
        }
    }

    private static void ValidateArray(JsonElement instance, JsonElement schema, string path, List<string> errors)
    {
        var count = instance.GetArrayLength();

        if (schema.TryGetProperty("minItems", out var minItems) && minItems.TryGetInt32(out var min) && count < min)
        {
            errors.Add($"{Display(path)}: array has {count} items, fewer than minItems {min}.");
        }
        if (schema.TryGetProperty("maxItems", out var maxItems) && maxItems.TryGetInt32(out var max) && count > max)
        {
            errors.Add($"{Display(path)}: array has {count} items, more than maxItems {max}.");
        }

        if (schema.TryGetProperty("items", out var itemsSchema) && itemsSchema.ValueKind == JsonValueKind.Object)
        {
            var index = 0;
            foreach (var item in instance.EnumerateArray())
            {
                Validate(item, itemsSchema, $"{path}/{index}", errors);
                index++;
            }
        }
    }

    private static bool JsonEquals(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind)
        {
            // Treat numerically-equal numbers as equal even if raw text differs (1 vs 1.0).
            if (a.ValueKind == JsonValueKind.Number && b.ValueKind == JsonValueKind.Number) { }
            else return false;
        }

        return a.ValueKind switch
        {
            JsonValueKind.Number => a.TryGetDouble(out var da) && b.TryGetDouble(out var db) && da == db,
            JsonValueKind.String => a.GetString() == b.GetString(),
            JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null => true,
            JsonValueKind.Array => ArrayEquals(a, b),
            JsonValueKind.Object => ObjectEquals(a, b),
            _ => a.GetRawText() == b.GetRawText()
        };
    }

    private static bool ArrayEquals(JsonElement a, JsonElement b)
    {
        if (a.GetArrayLength() != b.GetArrayLength()) return false;
        using var ea = a.EnumerateArray();
        using var eb = b.EnumerateArray();
        while (ea.MoveNext() && eb.MoveNext())
        {
            if (!JsonEquals(ea.Current, eb.Current)) return false;
        }
        return true;
    }

    private static bool ObjectEquals(JsonElement a, JsonElement b)
    {
        var bProps = b.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
        var aCount = 0;
        foreach (var pa in a.EnumerateObject())
        {
            aCount++;
            if (!bProps.TryGetValue(pa.Name, out var bv) || !JsonEquals(pa.Value, bv)) return false;
        }
        return aCount == bProps.Count;
    }

    private static string Display(string path) => string.IsNullOrEmpty(path) ? "(root)" : path;

    private static string TypeName(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Object => "object",
        JsonValueKind.Array => "array",
        JsonValueKind.String => "string",
        JsonValueKind.Number => "number",
        JsonValueKind.True or JsonValueKind.False => "boolean",
        JsonValueKind.Null => "null",
        _ => e.ValueKind.ToString().ToLowerInvariant()
    };
}
