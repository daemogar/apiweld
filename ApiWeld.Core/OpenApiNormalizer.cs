using System.Text.Json.Nodes;

namespace ApiWeld.Core;

/// <summary>Normalizes an OpenAPI description before code is generated from it.</summary>
/// <remarks>What each stage does and why the order matters: see README.md, "Order of the passes".</remarks>
public static class OpenApiNormalizer
{
	/// <summary>
	/// The description with formats canonicalized, unions collapsed and identical
	/// component schemas folded onto one name.
	/// </summary>
	/// <param name="documentText">The description as written. Format substitution is
	/// applied to the text, so this takes the document rather than a parsed tree.</param>
	/// <param name="resourceName">Names the primary get-response, which wins when
	/// identical schemas are folded.</param>
	public static JsonObject Normalize(string documentText, string resourceName)
	{
		var mapped = FormatMap.Apply(documentText);
		var collapsed = (JsonObject)UnionCollapse.Collapse(JsonNode.Parse(mapped))!;

		return SchemaDeduplicator.Deduplicate(collapsed, resourceName);
	}
}
