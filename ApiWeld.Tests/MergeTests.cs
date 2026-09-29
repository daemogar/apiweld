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

	// Review Focus 1. A variant expressed as a reference is not merged — the left
	// operand's reference survives and the right's is discarded. Pinned so the
	// behaviour is visible; changing it is a deliberate act with a failing test.
	[Fact]
	public void Keeps_only_the_first_reference_when_both_variants_are_references()
	{
		var merged = Merge(
			"""{ "$ref": "#/components/schemas/Left" }""",
			"""{ "$ref": "#/components/schemas/Right" }""");

		Assert.Equal("#/components/schemas/Left", (string?)merged["$ref"]);
	}
}
