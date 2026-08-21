using System.Text.Json.Nodes;
using LocalNEXUS.App.Models.Extensions;

namespace LocalNEXUS.App.Services.Extensions;

/// <summary>
/// Reads and writes an extension manifest.
/// </summary>
/// <remarks>
/// Hand written rather than reflected onto the record types, for one reason: a manifest is
/// authored by somebody who is not us, so every failure to read one has to name the field and
/// say what was expected. A deserialiser that returns null on a typo is how an extension author
/// spends an afternoon.
/// </remarks>
public static class ExtensionManifestJson
{
    /// <summary>The file an extension folder is expected to contain.</summary>
    public const string FileName = "localnexus.extension.json";

    /// <summary>
    /// Parses a manifest.
    /// </summary>
    /// <exception cref="ExtensionException">A required field is missing or malformed.</exception>
    public static ExtensionManifest Read(JsonObject json)
    {
        var id = Required(json, "id");

        var contracts = new List<ExtensionContract>();

        if (json["contracts"] is JsonArray declared)
        {
            foreach (var entry in declared)
            {
                var name = entry?.GetValue<string>();

                if (!Enum.TryParse<ExtensionContract>(name, ignoreCase: true, out var contract))
                {
                    throw new ExtensionException(
                        $"'{id}' declares the contract '{name}', which does not exist. " +
                        $"The contracts are: {string.Join(", ", Enum.GetNames<ExtensionContract>())}.");
                }

                contracts.Add(contract);
            }
        }

        if (contracts.Count == 0)
        {
            throw new ExtensionException(
                $"'{id}' declares no contracts, so there is nothing the host could do with it. " +
                $"Add at least one of: {string.Join(", ", Enum.GetNames<ExtensionContract>())}.");
        }

        return new ExtensionManifest(
            id,
            json["name"]?.GetValue<string>() ?? id,
            json["version"]?.GetValue<string>() ?? "0.0.0",
            json["description"]?.GetValue<string>() ?? string.Empty,
            json["author"]?.GetValue<string>(),
            json["homepage"]?.GetValue<string>(),
            contracts,
            ReadTools(json["tools"] as JsonArray),
            ReadNodes(json["nodes"] as JsonArray, id),
            ReadPrerequisites(json["prerequisites"] as JsonArray, id),
            ReadLaunch(json["launch"] as JsonObject, id),
            json["deprecated"]?.GetValue<string>());
    }

    /// <summary>Writes a manifest back out, so a registry round trips exactly.</summary>
    public static JsonObject Write(ExtensionManifest manifest)
    {
        var json = new JsonObject
        {
            ["id"] = manifest.Id,
            ["name"] = manifest.Name,
            ["version"] = manifest.Version,
            ["description"] = manifest.Description,
            ["contracts"] = new JsonArray(manifest.Contracts.Select(c => JsonValue.Create(c.ToString())).ToArray<JsonNode?>()),
            ["launch"] = WriteLaunch(manifest.Launch)
        };

        if (manifest.Author is { } author)
        {
            json["author"] = author;
        }

        if (manifest.Homepage is { } homepage)
        {
            json["homepage"] = homepage;
        }

        if (manifest.Deprecated is { } deprecated)
        {
            json["deprecated"] = deprecated;
        }

        if (manifest.Tools.Count > 0)
        {
            json["tools"] = new JsonArray(manifest.Tools.Select(t => (JsonNode?)new JsonObject
            {
                ["name"] = t.Name,
                ["description"] = t.Description,
                ["inputSchema"] = t.InputSchema?.DeepClone()
            }).ToArray());
        }

        if (manifest.Nodes.Count > 0)
        {
            json["nodes"] = new JsonArray(manifest.Nodes.Select(n => (JsonNode?)new JsonObject
            {
                ["typeKey"] = n.TypeKey,
                ["displayName"] = n.DisplayName,
                ["description"] = n.Description,
                ["inputs"] = WritePins(n.Inputs),
                ["outputs"] = WritePins(n.Outputs),
                ["settingsSchema"] = n.SettingsSchema?.DeepClone()
            }).ToArray());
        }

        if (manifest.Prerequisites.Count > 0)
        {
            json["prerequisites"] = new JsonArray(manifest.Prerequisites.Select(p => (JsonNode?)new JsonObject
            {
                ["kind"] = p.Kind.ToString(),
                ["name"] = p.Name,
                ["reason"] = p.Reason,
                ["installCommand"] = p.InstallCommand,
                ["installArguments"] = p.InstallArguments is null
                    ? null
                    : new JsonArray(p.InstallArguments.Select(a => JsonValue.Create(a)).ToArray<JsonNode?>()),
                ["minimumVersion"] = p.MinimumVersion
            }).ToArray());
        }

        return json;
    }

    private static JsonArray WritePins(IReadOnlyList<PinContribution> pins)
        => new(pins.Select(p => (JsonNode?)new JsonObject
        {
            ["name"] = p.Name,
            ["type"] = p.Type.ToString()
        }).ToArray());

    private static JsonObject WriteLaunch(ExtensionLaunch launch)
    {
        var json = new JsonObject
        {
            ["command"] = launch.Command,
            ["arguments"] = new JsonArray(launch.Arguments.Select(a => JsonValue.Create(a)).ToArray<JsonNode?>())
        };

        if (launch.WorkingDirectory is { } directory)
        {
            json["workingDirectory"] = directory;
        }

        if (launch.Environment is { Count: > 0 } environment)
        {
            var env = new JsonObject();

            foreach (var pair in environment)
            {
                env[pair.Key] = pair.Value;
            }

            json["environment"] = env;
        }

        return json;
    }

    private static ExtensionLaunch ReadLaunch(JsonObject? json, string id)
    {
        if (json is null)
        {
            throw new ExtensionException($"'{id}' has no 'launch' section, so there is no way to start it.");
        }

        var command = json["command"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(command))
        {
            throw new ExtensionException($"'{id}' has a 'launch' section with no 'command' in it.");
        }

        var arguments = (json["arguments"] as JsonArray)?
            .Select(a => a?.GetValue<string>() ?? string.Empty)
            .ToList() ?? new List<string>();

        Dictionary<string, string>? environment = null;

        if (json["environment"] is JsonObject env)
        {
            environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var pair in env)
            {
                environment[pair.Key] = pair.Value?.GetValue<string>() ?? string.Empty;
            }
        }

        return new ExtensionLaunch(
            command,
            arguments,
            json["workingDirectory"]?.GetValue<string>(),
            environment);
    }

    private static IReadOnlyList<ToolContribution> ReadTools(JsonArray? tools)
        => tools?
            .OfType<JsonObject>()
            .Select(t => new ToolContribution(
                t["name"]?.GetValue<string>() ?? "unnamed",
                t["description"]?.GetValue<string>() ?? string.Empty,
                t["inputSchema"] as JsonObject))
            .ToList()
            ?? (IReadOnlyList<ToolContribution>)Array.Empty<ToolContribution>();

    private static IReadOnlyList<NodeContribution> ReadNodes(JsonArray? nodes, string id)
    {
        if (nodes is null)
        {
            return Array.Empty<NodeContribution>();
        }

        var read = new List<NodeContribution>();

        foreach (var entry in nodes.OfType<JsonObject>())
        {
            var typeKey = entry["typeKey"]?.GetValue<string>()
                ?? throw new ExtensionException($"A node contributed by '{id}' has no 'typeKey'.");

            read.Add(new NodeContribution(
                typeKey,
                entry["displayName"]?.GetValue<string>() ?? typeKey,
                entry["description"]?.GetValue<string>() ?? string.Empty,
                ReadPins(entry["inputs"] as JsonArray, typeKey),
                ReadPins(entry["outputs"] as JsonArray, typeKey),
                entry["settingsSchema"] as JsonObject));
        }

        return read;
    }

    private static IReadOnlyList<PinContribution> ReadPins(JsonArray? pins, string typeKey)
        => pins?
            .OfType<JsonObject>()
            .Select(p =>
            {
                var name = p["name"]?.GetValue<string>()
                    ?? throw new ExtensionException($"A pin on '{typeKey}' has no 'name'.");

                return new PinContribution(name, ExtensionPinTypes.Parse(p["type"]?.GetValue<string>(), typeKey, name));
            })
            .ToList()
            ?? (IReadOnlyList<PinContribution>)Array.Empty<PinContribution>();

    private static IReadOnlyList<ExtensionPrerequisite> ReadPrerequisites(JsonArray? prerequisites, string id)
    {
        if (prerequisites is null)
        {
            return Array.Empty<ExtensionPrerequisite>();
        }

        var read = new List<ExtensionPrerequisite>();

        foreach (var entry in prerequisites.OfType<JsonObject>())
        {
            var kindName = entry["kind"]?.GetValue<string>();

            if (!Enum.TryParse<PrerequisiteKind>(kindName, ignoreCase: true, out var kind))
            {
                throw new ExtensionException(
                    $"'{id}' declares a prerequisite of kind '{kindName}', which does not exist. " +
                    $"The kinds are: {string.Join(", ", Enum.GetNames<PrerequisiteKind>())}.");
            }

            read.Add(new ExtensionPrerequisite(
                kind,
                entry["name"]?.GetValue<string>() ?? throw new ExtensionException($"A prerequisite of '{id}' has no 'name'."),
                entry["reason"]?.GetValue<string>() ?? string.Empty,
                entry["installCommand"]?.GetValue<string>(),
                (entry["installArguments"] as JsonArray)?.Select(a => a?.GetValue<string>() ?? string.Empty).ToList(),
                entry["minimumVersion"]?.GetValue<string>()));
        }

        return read;
    }

    private static string Required(JsonObject json, string field)
    {
        var value = json[field]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ExtensionException($"The manifest has no '{field}', which every extension needs.");
        }

        return value;
    }
}
