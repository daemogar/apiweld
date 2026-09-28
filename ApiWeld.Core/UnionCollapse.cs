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
}
