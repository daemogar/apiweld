using System.Text.Json.Nodes;

namespace ApiWeld.Core;

/// <summary>One permissive schema in place of every oneOf/anyOf.</summary>
/// <remarks>The rules and the reasoning behind them: README.md, "Union collapse rules".</remarks>
internal static class UnionCollapse
{
	static readonly string[] Unions = ["oneOf", "anyOf"];

	static readonly string[] Constraints = ["enum", "pattern", "format"];

	const string Local = "#/";

	/// <summary>Whether a variant is a "no value here" branch rather than a shape the union offers.</summary>
	public static bool IsAbsent(JsonNode? variant)
	{
		if (variant is JsonValue value && value.TryGetValue<bool>(out var accepts))
			return !accepts;

		if (variant is not JsonObject schema)
			return false;

		if ((int?)schema["maxProperties"] == 0)
			return true;

		if (Types(schema) is { Count: 1 } only && only.Contains("null"))
			return true;

		var type = Single(schema);

		if (type == "object" && schema["properties"] is null)
			return true;

		if (type != "string")
			return false;

		return (int?)schema["maxLength"] == 0
			|| (AllowsNull(schema)
				&& schema["enum"] is null
				&& schema["pattern"] is null);
	}

	/// <summary>Two sibling variants as one schema.</summary>
	public static JsonObject Merge(JsonObject left, JsonObject right)
	{
		JsonObject merged = [];

		foreach (var (name, value) in left)
			merged[name] = value?.DeepClone();

		foreach (var name in Constraints)
			if (merged.ContainsKey(name) != right.ContainsKey(name))
				merged.Remove(name);

		// A variant that requires nothing makes nothing required.
		if (!right.ContainsKey("required"))
			merged.Remove("required");

		foreach (var (name, value) in right)
		{
			// A constraint absent here was either never present or already dropped by an
			// earlier variant, and re-adding it would let the last variant in the list win.
			if (!merged.TryGetPropertyValue(name, out var current))
			{
				if (!Constraints.Contains(name) && name is not "required")
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
			else if (name is "type")
				MergeType(merged, current, value);
			else if (name is "items" && current is JsonObject one && value is JsonObject two)
				merged[name] = Merge(one, two);
			else if (Constraints.Contains(name) && !JsonNode.DeepEquals(current, value))
				merged.Remove(name);
		}

		return merged;
	}

	/// <summary>One permissive schema in place of every oneOf/anyOf in the tree.</summary>
	public static JsonNode? Collapse(JsonNode? node) => Collapse(node, node as JsonObject, []);

	static JsonNode? Collapse(JsonNode? node, JsonObject? document, HashSet<string> resolving)
	{
		if (node is JsonArray array)
			return new JsonArray([.. array.Select(p => Collapse(p, document, resolving))]);

		if (node is not JsonObject source)
			return node?.DeepClone();

		if (Array.Find(Unions, keyword => source[keyword] is JsonArray) is not { } union)
		{
			JsonObject result = [];

			foreach (var (name, value) in source)
				result[name] = Collapse(value, document, resolving);

			return result;
		}

		var variants = (JsonArray)source[union]!;
		var kept = variants.Where(p => !IsAbsent(p)).ToArray();

		// Whether a branch said the value may arrive blank, which is worth carrying:
		// it makes the surviving branch's format and pattern a possibility rather
		// than a promise.
		var emptiable = kept.Length < variants.Count;

		if (kept.Length == 0)
		{
			kept = [.. variants];
			emptiable = false;
		}

		JsonObject merged;

		if (kept.Any(p => p is JsonValue value && value.TryGetValue<bool>(out var accepts) && accepts))
			merged = [];
		else if (kept.Length == 1)
			merged = Collapse(kept[0], document, resolving) as JsonObject ?? [];
		else
		{
			// Several variants merge by shape, so each reference stands in for the schema it names.
			var shapes = kept.Select(p => Inline(p, document, resolving)).ToArray();

			merged = shapes[0];

			foreach (var shape in shapes.Skip(1))
				merged = Merge(merged, shape);
		}

		// What is written beside the union applies alongside it, and the schema's own words win.
		JsonObject siblings = [];

		foreach (var (name, value) in source)
			if (name != union)
				siblings[name] = value?.DeepClone();

		if (Collapse(siblings, document, resolving) is JsonObject beside)
			Overlay(merged, beside);

		if (emptiable && Single(merged) == "string")
		{
			merged.Remove("format");
			merged.Remove("pattern");
		}

		return merged;
	}

	/// <summary>A variant as a collapsed schema, with a local reference replaced by its target.</summary>
	static JsonObject Inline(JsonNode? variant, JsonObject? document, HashSet<string> resolving)
	{
		if (variant is JsonObject reference
			&& reference["$ref"] is JsonValue pointer && pointer.TryGetValue<string>(out var target)
			&& target.StartsWith(Local, StringComparison.Ordinal)
			&& Resolve(document, target) is JsonObject named
			&& resolving.Add(target))
		{
			try
			{
				var shape = Collapse(named, document, resolving) as JsonObject ?? [];
				JsonObject beside = [];

				foreach (var (name, value) in reference)
					if (name is not "$ref")
						beside[name] = value?.DeepClone();

				Overlay(shape, beside);

				return shape;
			}
			finally
			{
				resolving.Remove(target);
			}
		}

		return Collapse(variant, document, resolving) as JsonObject ?? [];
	}

	static JsonNode? Resolve(JsonObject? document, string target)
	{
		JsonNode? node = document;

		foreach (var segment in target[Local.Length..].Split('/'))
			node = (node as JsonObject)?[segment.Replace("~1", "/").Replace("~0", "~")];

		return node;
	}

	/// <summary>Lays the schema's own keywords over its merged variants: properties and required add, the rest replace.</summary>
	static void Overlay(JsonObject merged, JsonObject siblings)
	{
		foreach (var (name, value) in siblings)
		{
			if (name is "properties" && merged[name] is JsonObject target && value is JsonObject extra)
			{
				JsonObject combined = [];

				foreach (var (property, schema) in extra)
					combined[property] = target[property] is JsonObject a && schema is JsonObject b
						? OverlaidCopy(a, b)
						: schema?.DeepClone();

				foreach (var (property, schema) in target)
					if (!combined.ContainsKey(property))
						combined[property] = schema?.DeepClone();

				merged[name] = combined;
			}
			else if (name is "required" && merged[name] is JsonArray have && value is JsonArray more)
				merged[name] = new JsonArray([.. have.Concat(more)
					.Select(p => (string?)p)
					.Distinct()
					.Select(p => (JsonNode?)JsonValue.Create(p))]);
			else
				merged[name] = value?.DeepClone();
		}
	}

	static JsonObject OverlaidCopy(JsonObject schema, JsonObject over)
	{
		var copy = (JsonObject)schema.DeepClone();
		Overlay(copy, over);

		return copy;
	}

	/// <summary>Merges two <c>type</c> keywords: one real type survives, widened from integer to number; two or more drop it.</summary>
	static void MergeType(JsonObject merged, JsonNode? left, JsonNode? right)
	{
		var all = Types(left).Concat(Types(right)).ToHashSet(StringComparer.Ordinal);
		var nullable = all.Remove("null");

		if (all.Contains("number"))
			all.Remove("integer");

		if (all.Count != 1)
		{
			merged.Remove("type");
			return;
		}

		var type = all.Single();

		merged["type"] = nullable ? new JsonArray(type, "null") : JsonValue.Create(type);
	}

	/// <summary>The declared types: one for a string, each entry of an OpenAPI 3.1 array, none when absent.</summary>
	static HashSet<string> Types(JsonNode? type)
		=> type switch
		{
			JsonValue value when value.TryGetValue<string>(out var name) => [name],
			JsonArray names => [.. names.OfType<JsonValue>().Select(p => p.TryGetValue<string>(out var name) ? name : null).OfType<string>()],
			_ => [],
		};

	static HashSet<string>? Types(JsonObject schema) => schema.ContainsKey("type") ? Types(schema["type"]) : null;

	/// <summary>The one non-null type a schema declares, or null.</summary>
	static string? Single(JsonObject schema)
		=> Types(schema)?.Where(p => p != "null").ToArray() is [var only] ? only : null;

	static bool AllowsNull(JsonObject schema)
		=> (bool?)(schema["nullable"] as JsonValue) == true || Types(schema)?.Contains("null") == true;
}
