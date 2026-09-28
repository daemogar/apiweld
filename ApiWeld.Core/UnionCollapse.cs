using System.Text.Json.Nodes;

namespace ApiWeld.Core;

/// <summary>One permissive schema in place of every oneOf/anyOf.</summary>
/// <remarks>The rules and the reasoning behind them: README.md, "Union collapse rules".</remarks>
public static class UnionCollapse
{
	/// <summary>Whether a variant is a "no value here" branch rather than a shape the union offers.</summary>
	public static bool IsAbsent(JsonNode? variant)
	{
		if (variant is not JsonObject schema)
			return false;

		if ((int?)schema["maxProperties"] == 0)
			return true;

		if ((string?)schema["type"] == "object" && schema["properties"] is null)
			return true;

		if ((string?)schema["type"] != "string")
			return false;

		return (int?)schema["maxLength"] == 0
			|| ((bool?)schema["nullable"] == true
				&& schema["enum"] is null
				&& schema["pattern"] is null);
	}

	/// <summary>Two sibling variants as one schema.</summary>
	public static JsonObject Merge(JsonObject left, JsonObject right)
	{
		string[] constraints = ["enum", "pattern", "format"];

		JsonObject merged = [];

		foreach (var (name, value) in left)
			merged[name] = value?.DeepClone();

		foreach (var name in constraints)
			if (merged.ContainsKey(name) != right.ContainsKey(name))
				merged.Remove(name);

		foreach (var (name, value) in right)
		{
			// A constraint absent here was either never present or already dropped by an
			// earlier variant, and re-adding it would let the last variant in the list win.
			if (!merged.TryGetPropertyValue(name, out var current))
			{
				if (!constraints.Contains(name))
					merged[name] = value?.DeepClone();

				continue;
			}

			if (name is "properties" && current is JsonObject target && value is JsonObject additions)
			{
				foreach (var (property, schema) in additions)
					target[property] = target[property] is JsonObject a && schema is JsonObject b
						? Merge(a, b)
						: schema?.DeepClone();
			}
			else if (name is "required" && current is JsonArray required && value is JsonArray other)
			{
				var shared = other.Select(p => (string?)p).ToHashSet();

				merged[name] = new JsonArray([.. required
					.Where(p => shared.Contains((string?)p))
					.Select(p => p!.DeepClone())]);
			}
			else if (name is "items" && current is JsonObject one && value is JsonObject two)
				merged[name] = Merge(one, two);
			else if (constraints.Contains(name) && !JsonNode.DeepEquals(current, value))
				merged.Remove(name);
		}

		return merged;
	}
}
