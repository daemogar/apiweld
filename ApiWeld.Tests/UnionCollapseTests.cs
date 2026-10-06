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

	[Fact]
	public void A_variant_that_accepts_anything_makes_the_union_accept_anything()
	{
		var result = Collapse("""
			{ "description": "Any value.", "oneOf": [ true, { "type": "integer" } ] }
			""");

		Assert.False(result.ContainsKey("type"));
		Assert.Equal("Any value.", (string?)result["description"]);
	}

	[Fact]
	public void A_variant_that_accepts_nothing_is_dropped()
	{
		var result = Collapse("""
			{ "oneOf": [ false, { "type": "integer" } ] }
			""");

		Assert.Equal("integer", (string?)result["type"]);
	}

	[Fact]
	public void Untypes_variants_of_different_scalar_types()
	{
		var result = Collapse("""
			{ "oneOf": [ { "type": "string" }, { "type": "integer" } ] }
			""");

		Assert.False(result.ContainsKey("type"));
	}

	[Fact]
	public void Collapses_a_null_type_branch_like_any_absent_branch()
	{
		var result = Collapse("""
			{ "oneOf": [ { "type": "null" }, { "type": "string", "format": "date-time" } ] }
			""");

		Assert.Equal("string", (string?)result["type"]);
		Assert.False(result.ContainsKey("format"));
	}

	[Fact]
	public void Collapses_variants_written_with_type_arrays()
	{
		var result = Collapse("""
			{ "oneOf": [ { "type": ["string", "null"], "enum": ["a"] }, { "type": "string", "enum": ["a"] } ] }
			""");

		Assert.Equal(["string", "null"], ((JsonArray)result["type"]!).Select(p => (string?)p));
	}

	[Fact]
	public void Keeps_the_keywords_written_beside_the_union()
	{
		var result = Collapse("""
			{
			  "nullable": true, "readOnly": true, "deprecated": true, "default": "x", "example": "y",
			  "oneOf": [ { "type": "string", "maxLength": 0 }, { "type": "string" } ]
			}
			""");

		Assert.True((bool?)result["nullable"]);
		Assert.True((bool?)result["readOnly"]);
		Assert.True((bool?)result["deprecated"]);
		Assert.Equal("x", (string?)result["default"]);
		Assert.Equal("y", (string?)result["example"]);
		Assert.Equal("string", (string?)result["type"]);
	}

	[Fact]
	public void Adds_the_properties_written_beside_the_union_to_the_variants()
	{
		var result = Collapse("""
			{
			  "type": "object", "required": ["id"], "properties": { "id": { "type": "string" } },
			  "oneOf": [ { "properties": { "a": { "type": "string" } } }, { "properties": { "b": { "type": "string" } } } ]
			}
			""");

		Assert.Equal(["id", "a", "b"], ((JsonObject)result["properties"]!).Select(p => p.Key));
		Assert.Equal(["id"], ((JsonArray)result["required"]!).Select(p => (string?)p));
	}

	[Fact]
	public void Collapses_a_oneOf_and_an_anyOf_on_the_same_schema()
	{
		var result = Collapse("""
			{
			  "oneOf": [ { "maxProperties": 0 }, { "type": "object", "properties": { "a": { "type": "string" } } } ],
			  "anyOf": [ { "properties": { "b": { "type": "string" } } }, { "properties": { "c": { "type": "string" } } } ]
			}
			""");

		Assert.False(result.ContainsKey("oneOf"));
		Assert.False(result.ContainsKey("anyOf"));
		Assert.Equal(["a", "b", "c"], ((JsonObject)result["properties"]!).Select(p => p.Key).Order());
	}

	const string Referenced = """
		{
		  "paths": { "/a": { "get": { "responses": { "200": { "content": { "application/json": { "schema": UNION } } } } } } },
		  "components": {
		    "schemas": {
		      "A": { "type": "object", "required": ["a"], "properties": { "a": { "type": "string" } } },
		      "B": { "type": "object", "properties": { "b": { "type": "string" } } },
		      "Node": { "type": "object", "properties": { "next": { "oneOf": [ { "$ref": "#/components/schemas/Node" }, { "$ref": "#/components/schemas/B" } ] } } }
		    }
		  }
		}
		""";

	static JsonObject CollapseUnion(string union)
	{
		var document = (JsonObject)UnionCollapse.Collapse(JsonNode.Parse(Referenced.Replace("UNION", union)))!;

		return (JsonObject)document["paths"]!["/a"]!["get"]!["responses"]!["200"]!["content"]!["application/json"]!["schema"]!;
	}

	[Fact]
	public void Merges_referenced_variants_by_the_shapes_they_name()
	{
		var result = CollapseUnion("""{ "oneOf": [ { "$ref": "#/components/schemas/A" }, { "$ref": "#/components/schemas/B" } ] }""");

		Assert.False(result.ContainsKey("$ref"));
		Assert.Equal(["a", "b"], ((JsonObject)result["properties"]!).Select(p => p.Key));
		Assert.False(result.ContainsKey("required"));
	}

	[Fact]
	public void Merges_a_referenced_variant_with_an_inline_one()
	{
		var result = CollapseUnion("""{ "oneOf": [ { "$ref": "#/components/schemas/A" }, { "type": "object", "properties": { "c": { "type": "string" } } } ] }""");

		Assert.False(result.ContainsKey("$ref"));
		Assert.Equal(["a", "c"], ((JsonObject)result["properties"]!).Select(p => p.Key));
	}

	[Fact]
	public void Keeps_a_lone_surviving_reference_as_a_reference()
	{
		var result = CollapseUnion("""{ "oneOf": [ { "maxProperties": 0 }, { "$ref": "#/components/schemas/A" } ] }""");

		Assert.Equal("#/components/schemas/A", (string?)result["$ref"]);
	}

	[Fact]
	public void Stops_at_a_union_that_refers_to_itself()
	{
		var result = CollapseUnion("""{ "$ref": "#/components/schemas/Node" }""");

		Assert.Equal("#/components/schemas/Node", (string?)result["$ref"]);
	}
}
