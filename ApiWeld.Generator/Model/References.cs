using System.Text.Json.Nodes;

namespace ApiWeld.Generator.Model;

/// <summary>Follows local <c>$ref</c> pointers.</summary>
internal static class References
{
	/// <summary>The node a reference chain ends at, and the component schema it passed through last, if any.</summary>
	public static (JsonObject? Node, string? Component) Resolve(JsonNode? node, JsonObject document)
	{
		string? component = null;

		for (var hops = 0; JsonText.String(node, "$ref") is { } pointer; hops++)
		{
			if (hops == 32 || !pointer.StartsWith("#/", StringComparison.Ordinal))
				return (null, component);

			var segments = pointer[2..].Split('/').Select(segment => segment.Replace("~1", "/").Replace("~0", "~")).ToArray();

			if (segments is ["components", "schemas", var name])
				component = name;

			node = segments.Aggregate<string, JsonNode?>(document, Step);
		}

		return (node as JsonObject, component);
	}

	static JsonNode? Step(JsonNode? current, string segment) => current switch
	{
		JsonObject map => map[segment],
		JsonArray list when int.TryParse(segment, out var index) && index < list.Count => list[index],
		_ => null
	};
}

/// <summary>Reads typed values out of description nodes.</summary>
internal static class JsonText
{
	/// <summary>The string at <paramref name="key"/>, or null.</summary>
	public static string? String(JsonNode? node, string key)
		=> (node as JsonObject)?[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

	/// <summary>The schema's one non-null type, from a string or an OpenAPI 3.1 type array, or null.</summary>
	public static string? Type(JsonNode? node)
		=> (node as JsonObject)?["type"] is JsonArray types
			? types.OfType<JsonValue>().Select(p => p.TryGetValue<string>(out var type) ? type : null).OfType<string>().Where(p => p != "null").ToArray() is [var only] ? only : null
			: String(node, "type");

	/// <summary>Whether the boolean at <paramref name="key"/> is true.</summary>
	public static bool Flag(JsonNode? node, string key)
		=> (node as JsonObject)?[key] is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;
}
