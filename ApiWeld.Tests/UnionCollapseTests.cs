using System.Text.Json.Nodes;

using ApiWeld.Core;

namespace ApiWeld.Tests;

public class UnionCollapseTests
{
	static JsonObject Collapse(string json)
		=> (JsonObject)UnionCollapse.Collapse(JsonNode.Parse(json))!;

	[Fact]
	public void Drops_the_absent_branch_and_keeps_the_shape()
	{
		var result = Collapse("""
			{
			  "oneOf": [
			    { "maxProperties": 0 },
			    { "type": "object", "properties": { "id": { "type": "string" } } }
			  ]
			}
			""");

		Assert.False(result.ContainsKey("oneOf"));
		Assert.Equal("object", (string?)result["type"]);
		Assert.True(((JsonObject)result["properties"]!).ContainsKey("id"));
	}

	[Fact]
	public void Collapses_an_anyOf_the_same_way_as_a_oneOf()
	{
		var result = Collapse("""
			{
			  "anyOf": [
			    { "type": "string", "maxLength": 0 },
			    { "type": "string", "description": "A code" }
			  ]
			}
			""");

		Assert.False(result.ContainsKey("anyOf"));
		Assert.Equal("string", (string?)result["type"]);
	}

	// The rule that keeps a timestamp a plain string rather than a date: a branch
	// saying the value may arrive blank makes the survivor's constraints a
	// possibility rather than a promise.
	[Fact]
	public void An_empty_string_branch_strips_the_survivors_format_and_pattern()
	{
		var result = Collapse("""
			{
			  "oneOf": [
			    { "type": "string", "maxLength": 0 },
			    { "type": "string", "format": "date-time", "pattern": "^2" }
			  ]
			}
			""");

		Assert.Equal("string", (string?)result["type"]);
		Assert.False(result.ContainsKey("format"));
		Assert.False(result.ContainsKey("pattern"));
	}

	[Fact]
	public void A_surviving_string_keeps_its_format_when_no_branch_was_dropped()
	{
		var result = Collapse("""
			{
			  "oneOf": [
			    { "type": "string", "format": "date" },
			    { "type": "string", "format": "date" }
			  ]
			}
			""");

		Assert.Equal("date", (string?)result["format"]);
	}

	[Fact]
	public void Keeps_every_branch_when_all_of_them_are_absent()
	{
		var result = Collapse("""
			{
			  "oneOf": [
			    { "type": "string", "maxLength": 0 },
			    { "type": "string", "nullable": true }
			  ]
			}
			""");

		Assert.Equal("string", (string?)result["type"]);
		Assert.True((bool?)result["nullable"]);
	}

	[Fact]
	public void The_propertys_own_title_and_description_beat_a_variants()
	{
		var result = Collapse("""
			{
			  "title": "Country",
			  "description": "The postal region.",
			  "oneOf": [
			    { "maxProperties": 0 },
			    { "type": "object", "title": "Variant", "description": "A specific country.",
			      "properties": { "code": { "type": "string" } } }
			  ]
			}
			""");

		Assert.Equal("Country", (string?)result["title"]);
		Assert.Equal("The postal region.", (string?)result["description"]);
	}

	[Fact]
	public void Collapses_a_union_nested_inside_a_surviving_variant()
	{
		var result = Collapse("""
			{
			  "oneOf": [
			    { "maxProperties": 0 },
			    {
			      "type": "object",
			      "properties": {
			        "inner": { "oneOf": [ { "maxProperties": 0 }, { "type": "integer" } ] }
			      }
			    }
			  ]
			}
			""");

		var inner = (JsonObject)((JsonObject)result["properties"]!)["inner"]!;

		Assert.False(inner.ContainsKey("oneOf"));
		Assert.Equal("integer", (string?)inner["type"]);
	}

	[Fact]
	public void Leaves_a_document_with_no_union_untouched()
	{
		const string json = """{ "type": "object", "properties": { "id": { "type": "string" } } }""";

		Assert.True(JsonNode.DeepEquals(JsonNode.Parse(json), UnionCollapse.Collapse(JsonNode.Parse(json))));
	}

	[Fact]
	public void Walks_into_arrays()
	{
		var result = (JsonObject)UnionCollapse.Collapse(JsonNode.Parse("""
			{ "allOf": [ { "oneOf": [ { "maxProperties": 0 }, { "type": "integer" } ] } ] }
			"""))!;

		Assert.Equal("integer", (string?)((JsonArray)result["allOf"]!)[0]!["type"]);
	}

	// Review Focus 2. A non-object variant is coerced to an empty object and
	// contributes nothing. Pinned rather than fixed.
	[Fact]
	public void A_variant_that_is_not_an_object_contributes_nothing()
	{
		var result = Collapse("""
			{ "oneOf": [ true, { "type": "integer" } ] }
			""");

		Assert.Equal("integer", (string?)result["type"]);
	}
}
