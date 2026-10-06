using System.Text.Json.Nodes;

namespace ApiWeld.Core;

/// <summary>Canonical spellings for formats a description may use non-standard names for.</summary>
/// <remarks>Which values count as schema keywords: see README.md, "Format substitution".</remarks>
internal static class FormatMap
{
	static readonly Dictionary<string, string> Mappings = new(StringComparer.Ordinal)
	{
		["guid"] = "uuid",
		["email"] = "string",
	};

	/// <summary>Keywords whose values are data, never schemas.</summary>
	static readonly HashSet<string> Data = new(StringComparer.Ordinal) { "example", "examples", "default", "enum", "const" };

	/// <summary>The tree with every recognized <c>format</c> keyword replaced by its canonical spelling, in place.</summary>
	public static JsonNode? Apply(JsonNode? node)
	{
		if (node is JsonArray array)
		{
			foreach (var item in array)
				Apply(item);
		}
		else if (node is JsonObject schema)
		{
			foreach (var (name, value) in schema.ToArray())
			{
				if (Data.Contains(name) || name.StartsWith("x-", StringComparison.Ordinal))
					continue;

				if (name is "format" && value is JsonValue format && format.TryGetValue<string>(out var spelling)
					&& Mappings.TryGetValue(spelling, out var canonical))
					schema[name] = canonical;
				else
					Apply(value);
			}
		}

		return node;
	}
}
