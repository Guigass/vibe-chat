using System.Globalization;
using System.Text.Json.Nodes;

namespace VibeChat.Integrations;

/// <summary>
/// JSON Schema subset used by the public catalog: object, properties, required,
/// additionalProperties, items, enum, format uuid/date-time, minimum/maximum.
/// </summary>
public static class EventSchemaSubset
{
    public static bool TryValidate(JsonObject schema, JsonNode? instance, out string error)
    {
        error = "";
        return Validate(schema, instance, "$", out error);
    }

    public static bool TryReadInt(JsonValue value, out int number)
    {
        if (value.TryGetValue<int>(out number))
        {
            return true;
        }

        if (value.TryGetValue<long>(out var wide) && wide is >= int.MinValue and <= int.MaxValue)
        {
            number = (int)wide;
            return true;
        }

        number = 0;
        return false;
    }

    private static bool Validate(JsonObject schema, JsonNode? instance, string path, out string error)
    {
        error = "";
        var type = schema["type"]?.GetValue<string>();
        if (!TypeMatches(type, instance))
        {
            error = $"{path}:type";
            return false;
        }

        if (schema["enum"] is JsonArray allowed && !EnumMatches(allowed, instance))
        {
            error = $"{path}:enum";
            return false;
        }

        if (schema["format"]?.GetValue<string>() is string format && !FormatMatches(format, instance))
        {
            error = $"{path}:format";
            return false;
        }

        if (type == "object" && instance is JsonObject obj)
        {
            return ValidateObject(schema, obj, path, out error);
        }

        if (type == "array" && instance is JsonArray array && schema["items"] is JsonObject itemSchema)
        {
            for (var i = 0; i < array.Count; i++)
            {
                if (!Validate(itemSchema, array[i], $"{path}[{i}]", out error))
                {
                    return false;
                }
            }
        }

        if (type is "integer" or "number" && instance is JsonValue number)
        {
            if (!TryDecimal(number, out var numeric))
            {
                error = $"{path}:number";
                return false;
            }

            if (schema["minimum"] is JsonValue minNode && TryDecimal(minNode, out var min) && numeric < min)
            {
                error = $"{path}:minimum";
                return false;
            }

            if (schema["maximum"] is JsonValue maxNode && TryDecimal(maxNode, out var max) && numeric > max)
            {
                error = $"{path}:maximum";
                return false;
            }
        }

        return true;
    }

    private static bool ValidateObject(JsonObject schema, JsonObject instance, string path, out string error)
    {
        error = "";
        var properties = schema["properties"] as JsonObject ?? new JsonObject();
        if (schema["required"] is JsonArray required)
        {
            foreach (var item in required)
            {
                var name = item!.GetValue<string>();
                if (!instance.ContainsKey(name) || instance[name] is null)
                {
                    error = $"{path}.{name}:required";
                    return false;
                }
            }
        }

        var additional = schema["additionalProperties"]?.GetValue<bool>() ?? true;
        foreach (var pair in instance)
        {
            if (properties[pair.Key] is JsonObject propertySchema)
            {
                if (!Validate(propertySchema, pair.Value, $"{path}.{pair.Key}", out error))
                {
                    return false;
                }
            }
            else if (!additional)
            {
                error = $"{path}.{pair.Key}:additional";
                return false;
            }
        }

        return true;
    }

    private static bool TypeMatches(string? type, JsonNode? instance) => type switch
    {
        "object" => instance is JsonObject,
        "array" => instance is JsonArray,
        "string" => instance is JsonValue value && value.TryGetValue<string>(out _),
        "boolean" => instance is JsonValue flag && flag.TryGetValue<bool>(out _),
        "integer" => instance is JsonValue integer && IsInteger(integer),
        "number" => instance is JsonValue number && TryDecimal(number, out _),
        "null" => instance is null,
        _ => false
    };

    private static bool IsInteger(JsonValue value)
    {
        if (value.TryGetValue<int>(out _) || value.TryGetValue<long>(out _))
        {
            return true;
        }

        return value.TryGetValue<double>(out var number)
            && !double.IsInfinity(number)
            && !double.IsNaN(number)
            && number == Math.Truncate(number);
    }

    private static bool TryDecimal(JsonValue value, out decimal number)
    {
        if (value.TryGetValue<int>(out var integer))
        {
            number = integer;
            return true;
        }

        if (value.TryGetValue<long>(out var wide))
        {
            number = wide;
            return true;
        }

        if (value.TryGetValue<double>(out var floating) && !double.IsInfinity(floating) && !double.IsNaN(floating))
        {
            number = (decimal)floating;
            return true;
        }

        number = 0;
        return false;
    }

    private static bool EnumMatches(JsonArray allowed, JsonNode? instance)
    {
        foreach (var item in allowed)
        {
            if (JsonNode.DeepEquals(item, instance))
            {
                return true;
            }
        }

        return false;
    }

    private static bool FormatMatches(string format, JsonNode? instance)
    {
        if (instance is not JsonValue value || !value.TryGetValue<string>(out var text))
        {
            return false;
        }

        return format switch
        {
            "uuid" => Guid.TryParse(text, out _),
            "date-time" => DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _),
            _ => true
        };
    }
}

public static class EventSchemaCompatibility
{
    public static IReadOnlyList<string> BreakingChanges(string baselineJson, string currentJson, DateOnly asOf)
    {
        var baseline = JsonNode.Parse(baselineJson)!.AsObject();
        var current = JsonNode.Parse(currentJson)!.AsObject();
        var breaking = new List<string>();
        var currentVersions = Versions(current);
        foreach (var oldVersion in Versions(baseline))
        {
            var key = Key(oldVersion);
            var updated = currentVersions.FirstOrDefault(item => Key(item) == key);
            if (updated is null)
            {
                if (StillSupported(oldVersion, asOf))
                {
                    breaking.Add($"removed {key} while the deprecation window is open");
                }

                continue;
            }

            if (EarlierWindow(oldVersion, updated))
            {
                breaking.Add($"shorter deprecation window for {key}");
            }

            if (oldVersion["schema"] is JsonObject oldSchema && updated["schema"] is JsonObject newSchema)
            {
                breaking.AddRange(SchemaBreaking(oldSchema, newSchema, key));
            }
        }

        return breaking;
    }

    private static bool EarlierWindow(JsonObject baseline, JsonObject current)
    {
        var before = ReadDate(baseline["deprecatedUntil"]);
        var after = ReadDate(current["deprecatedUntil"]);
        return before is DateOnly oldDate && after is DateOnly newDate && newDate < oldDate;
    }

    private static bool StillSupported(JsonObject version, DateOnly asOf)
    {
        var status = version["status"]?.GetValue<string>();
        if (!string.Equals(status, "deprecated", StringComparison.Ordinal))
        {
            return true;
        }

        var until = ReadDate(version["deprecatedUntil"]);
        return until is null || until.Value > asOf;
    }

    private static DateOnly? ReadDate(JsonNode? node)
    {
        if (node is not JsonValue value || !value.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static List<JsonObject> Versions(JsonObject document) =>
        document["versions"]!.AsArray().Select(item => item!.AsObject()).ToList();

    private static string Key(JsonObject version) =>
        $"{version["eventType"]!.GetValue<string>()}@{version["schemaVersion"]!.GetValue<int>()}";

    private static IEnumerable<string> SchemaBreaking(JsonObject baseline, JsonObject current, string path)
    {
        var breaking = new List<string>();
        var oldType = baseline["type"]?.GetValue<string>();
        var newType = current["type"]?.GetValue<string>();
        if (!string.Equals(oldType, newType, StringComparison.Ordinal))
        {
            breaking.Add($"{path} type {oldType} -> {newType}");
            return breaking;
        }

        if (baseline["format"]?.GetValue<string>() is string oldFormat
            && !string.Equals(oldFormat, current["format"]?.GetValue<string>(), StringComparison.Ordinal))
        {
            breaking.Add($"{path} format changed");
        }

        if (current["format"] is not null && baseline["format"] is null)
        {
            breaking.Add($"{path} format added");
        }

        if (baseline["enum"] is JsonArray oldEnum && current["enum"] is JsonArray newEnum)
        {
            foreach (var item in oldEnum)
            {
                if (!newEnum.Any(candidate => JsonNode.DeepEquals(candidate, item)))
                {
                    breaking.Add($"{path} enum value removed");
                    break;
                }
            }
        }

        if (TighterBound(baseline, current, "minimum", greaterIsTighter: true)
            || TighterBound(baseline, current, "maximum", greaterIsTighter: false))
        {
            breaking.Add($"{path} numeric bound tightened");
        }

        if (!string.Equals(oldType, "object", StringComparison.Ordinal))
        {
            if (string.Equals(oldType, "array", StringComparison.Ordinal)
                && baseline["items"] is JsonObject oldItems
                && current["items"] is JsonObject newItems)
            {
                breaking.AddRange(SchemaBreaking(oldItems, newItems, path + "[]"));
            }

            return breaking;
        }

        var oldAdditional = baseline["additionalProperties"]?.GetValue<bool>() ?? true;
        var newAdditional = current["additionalProperties"]?.GetValue<bool>() ?? true;
        if (oldAdditional && !newAdditional)
        {
            breaking.Add($"{path} additionalProperties closed");
        }

        var oldRequired = Required(baseline);
        var newRequired = Required(current);
        foreach (var name in newRequired.Except(oldRequired, StringComparer.Ordinal))
        {
            breaking.Add($"{path}.{name} became required");
        }

        var oldProps = baseline["properties"] as JsonObject ?? new JsonObject();
        var newProps = current["properties"] as JsonObject ?? new JsonObject();
        foreach (var pair in oldProps)
        {
            if (newProps[pair.Key] is not JsonObject updated || pair.Value is not JsonObject previous)
            {
                breaking.Add($"{path}.{pair.Key} removed");
                continue;
            }

            breaking.AddRange(SchemaBreaking(previous, updated, $"{path}.{pair.Key}"));
        }

        return breaking;
    }

    private static bool TighterBound(JsonObject baseline, JsonObject current, string name, bool greaterIsTighter)
    {
        var oldNode = baseline[name];
        var newNode = current[name];
        if (newNode is null)
        {
            return false;
        }

        if (oldNode is null)
        {
            return true;
        }

        if (oldNode is not JsonValue oldValue || newNode is not JsonValue newValue
            || !EventSchemaSubset.TryReadInt(oldValue, out var oldNumber)
            || !EventSchemaSubset.TryReadInt(newValue, out var newNumber))
        {
            return false;
        }

        return greaterIsTighter ? newNumber > oldNumber : newNumber < oldNumber;
    }

    private static HashSet<string> Required(JsonObject schema)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (schema["required"] is JsonArray required)
        {
            foreach (var item in required)
            {
                names.Add(item!.GetValue<string>());
            }
        }

        return names;
    }
}
