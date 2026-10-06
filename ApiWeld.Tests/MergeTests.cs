using System.Text.Json.Nodes;

using ApiWeld.Core;

namespace ApiWeld.Tests;

public class MergeTests
{
	static JsonObject Parse(string json) => (JsonObject)JsonNode.Parse(json)!;

	static JsonObject Merge(string left, string right)
		=> UnionCollapse.Merge(Parse(left), Parse(right));

	[Fact]
	public void Offers_every_property_either_variant_declares()
	{
		var merged = Merge(
			"""{ "properties": { "a": { "type": "string" } } }""",
			"""{ "properties": { "b": { "type": "integer" } } }""");

		var properties = (JsonObject)merged["properties"]!;

		Assert.Equal(2, properties.Count);
		Assert.Equal("string", (string?)properties["a"]!["type"]);
		Assert.Equal("integer", (string?)properties["b"]!["type"]);
	}

	[Fact]
	public void Requires_only_what_both_variants_require()
	{
		var merged = Merge(
			"""{ "required": ["a", "b"] }""",
			"""{ "required": ["b", "c"] }""");

		Assert.Equal(["b"], ((JsonArray)merged["required"]!).Select(p => (string?)p));
	}

	[Fact]
	public void Drops_an_enum_the_variants_disagree_on()
	{
		var merged = Merge(
			"""{ "type": "string", "enum": ["a"] }""",
			"""{ "type": "string", "enum": ["b"] }""");

		Assert.False(merged.ContainsKey("enum"));
	}

	[Fact]
	public void Keeps_an_enum_the_variants_agree_on()
	{
		var merged = Merge(
			"""{ "type": "string", "enum": ["a"] }""",
			"""{ "type": "string", "enum": ["a"] }""");

		Assert.Equal(["a"], ((JsonArray)merged["enum"]!).Select(p => (string?)p));
	}

	[Fact]
	public void Drops_an_enum_only_one_variant_carries()
	{
		var merged = Merge(
			"""{ "type": "string", "enum": ["a"] }""",
			"""{ "type": "string" }""");

		Assert.False(merged.ContainsKey("enum"));
	}

	[Fact]
	public void Drops_a_pattern_only_one_variant_carries()
	{
		var merged = Merge(
			"""{ "type": "string", "pattern": "^a$" }""",
			"""{ "type": "string" }""");

		Assert.False(merged.ContainsKey("pattern"));
	}

	[Fact]
	public void Drops_a_format_only_the_second_variant_carries()
	{
		var merged = Merge(
			"""{ "type": "string" }""",
			"""{ "type": "string", "format": "date" }""");

		Assert.False(merged.ContainsKey("format"));
	}

	[Fact]
	public void Merges_a_property_that_both_variants_declare()
	{
		var merged = Merge(
			"""{ "properties": { "a": { "type": "string", "title": "Left" } } }""",
			"""{ "properties": { "a": { "type": "string", "maxLength": 4 } } }""");

		var a = (JsonObject)((JsonObject)merged["properties"]!)["a"]!;

		Assert.Equal("Left", (string?)a["title"]);
		Assert.Equal(4, (int?)a["maxLength"]);
	}

	[Fact]
	public void Merges_the_item_schema_of_two_arrays()
	{
		var merged = Merge(
			"""{ "type": "array", "items": { "properties": { "a": { "type": "string" } } } }""",
			"""{ "type": "array", "items": { "properties": { "b": { "type": "string" } } } }""");

		var properties = (JsonObject)merged["items"]!["properties"]!;

		Assert.Equal(2, properties.Count);
	}

	// Collapse resolves references before it merges; only a reference it cannot
	// resolve reaches Merge, and then the left operand's survives.
	[Fact]
	public void Keeps_only_the_first_reference_when_both_variants_are_references()
	{
		var merged = Merge(
			"""{ "$ref": "#/components/schemas/Left" }""",
			"""{ "$ref": "#/components/schemas/Right" }""");

		Assert.Equal("#/components/schemas/Left", (string?)merged["$ref"]);
	}

	// A variant with no required list requires nothing, so the merge requires nothing either.
	[Fact]
	public void Drops_a_required_list_only_one_variant_carries()
	{
		var leftCarries = Merge("""{ "required": ["a"] }""", """{}""");
		var rightCarries = Merge("""{}""", """{ "required": ["a"] }""");

		Assert.False(leftCarries.ContainsKey("required"));
		Assert.False(rightCarries.ContainsKey("required"));
	}

	[Fact]
	public void Drops_the_type_when_the_variants_disagree()
	{
		var merged = Merge("""{ "type": "string" }""", """{ "type": "integer" }""");

		Assert.False(merged.ContainsKey("type"));
	}

	[Fact]
	public void Widens_an_integer_and_a_number_to_a_number()
	{
		var merged = Merge("""{ "type": "integer" }""", """{ "type": "number" }""");

		Assert.Equal("number", (string?)merged["type"]);
	}

	[Fact]
	public void Keeps_null_in_a_merged_type_array()
	{
		var merged = Merge("""{ "type": ["string", "null"], "enum": ["a"] }""", """{ "type": "string", "enum": ["a"] }""");

		Assert.Equal(["string", "null"], ((JsonArray)merged["type"]!).Select(p => (string?)p));
	}

	[Fact]
	public void Drops_a_type_array_whose_real_types_disagree()
	{
		var merged = Merge("""{ "type": ["string", "null"] }""", """{ "type": "integer" }""");

		Assert.False(merged.ContainsKey("type"));
	}
}
