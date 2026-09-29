using System.Text.Json.Nodes;

using ApiWeld.Core;

namespace ApiWeld.Tests;

public class SchemaDeduplicatorTests
{
	static JsonObject Deduplicate(string json, string resource = "widgets")
		=> SchemaDeduplicator.Deduplicate((JsonObject)JsonNode.Parse(json)!, resource);

	const string TwoIdenticalSchemas = """
		{
		  "paths": {
		    "/widgets": {
		      "get": { "responses": { "200": { "schema": { "$ref": "#/components/schemas/widgets_get_response" } } } },
		      "post": { "requestBody": { "schema": { "$ref": "#/components/schemas/widgets_post_request" } } }
		    }
		  },
		  "components": {
		    "schemas": {
		      "widgets_get_response": { "type": "object", "properties": { "id": { "type": "string" } } },
		      "widgets_post_request": { "type": "object", "properties": { "id": { "type": "string" } } }
		    }
		  }
		}
		""";

	[Fact]
	public void Folds_identical_schemas_to_one_and_rewrites_references_to_it()
	{
		var result = Deduplicate(TwoIdenticalSchemas);
		var schemas = (JsonObject)result["components"]!["schemas"]!;

		Assert.True(schemas.ContainsKey("widgets_get_response"));
		Assert.False(schemas.ContainsKey("widgets_post_request"));

		var request = (string?)result["paths"]!["/widgets"]!["post"]!["requestBody"]!["schema"]!["$ref"];

		Assert.Equal("#/components/schemas/widgets_get_response", request);
	}

	[Fact]
	public void Prefers_the_resources_own_get_response_as_the_survivor()
	{
		var result = Deduplicate(TwoIdenticalSchemas);

		Assert.True(((JsonObject)result["components"]!["schemas"]!).ContainsKey("widgets_get_response"));
	}

	[Fact]
	public void Prefers_a_response_over_a_request_when_neither_is_the_get_response()
	{
		var result = Deduplicate("""
			{
			  "paths": {
			    "/a": { "get": { "schema": { "$ref": "#/components/schemas/other_response" } } },
			    "/b": { "post": { "schema": { "$ref": "#/components/schemas/other_request" } } }
			  },
			  "components": {
			    "schemas": {
			      "other_request": { "type": "object", "properties": { "id": { "type": "string" } } },
			      "other_response": { "type": "object", "properties": { "id": { "type": "string" } } }
			    }
			  }
			}
			""");

		var schemas = (JsonObject)result["components"]!["schemas"]!;

		Assert.True(schemas.ContainsKey("other_response"));
		Assert.False(schemas.ContainsKey("other_request"));
	}

	// The rule that stops a live type being renamed after a dead one.
	[Fact]
	public void Leaves_an_unreferenced_duplicate_alone()
	{
		var result = Deduplicate("""
			{
			  "paths": { "/a": { "get": { "schema": { "$ref": "#/components/schemas/live_response" } } } },
			  "components": {
			    "schemas": {
			      "live_response": { "type": "object", "properties": { "id": { "type": "string" } } },
			      "orphan": { "type": "object", "properties": { "id": { "type": "string" } } }
			    }
			  }
			}
			""");

		var schemas = (JsonObject)result["components"]!["schemas"]!;

		Assert.True(schemas.ContainsKey("live_response"));
		Assert.True(schemas.ContainsKey("orphan"));
	}

	[Fact]
	public void Does_not_merge_two_schemas_that_are_merely_similar()
	{
		var result = Deduplicate("""
			{
			  "paths": {
			    "/a": { "get": { "schema": { "$ref": "#/components/schemas/a_response" } } },
			    "/b": { "get": { "schema": { "$ref": "#/components/schemas/b_response" } } }
			  },
			  "components": {
			    "schemas": {
			      "a_response": { "type": "object", "properties": { "id": { "type": "string" } } },
			      "b_response": { "type": "object", "properties": { "id": { "type": "integer" } } }
			    }
			  }
			}
			""");

		var schemas = (JsonObject)result["components"]!["schemas"]!;

		Assert.Equal(2, schemas.Count);
	}

	[Fact]
	public void Follows_references_made_from_one_schema_to_another()
	{
		var result = Deduplicate("""
			{
			  "paths": { "/a": { "get": { "schema": { "$ref": "#/components/schemas/outer_response" } } } },
			  "components": {
			    "schemas": {
			      "outer_response": { "type": "object", "properties": { "inner": { "$ref": "#/components/schemas/inner_response" } } },
			      "inner_response": { "type": "object", "properties": { "id": { "type": "string" } } },
			      "inner_request": { "type": "object", "properties": { "id": { "type": "string" } } }
			    }
			  }
			}
			""");

		var schemas = (JsonObject)result["components"]!["schemas"]!;

		// inner_request is unreferenced, so it survives untouched rather than folding.
		Assert.True(schemas.ContainsKey("inner_response"));
		Assert.True(schemas.ContainsKey("inner_request"));
	}

	// Review Focus 4.
	[Fact]
	public void Returns_a_document_with_no_components_unchanged()
	{
		const string json = """{ "openapi": "3.0.0", "paths": {} }""";
		var result = Deduplicate(json);

		Assert.True(JsonNode.DeepEquals(JsonNode.Parse(json), result));
	}

	// Review Focus 3.
	[Fact]
	public void Leaves_a_reference_to_a_schema_that_does_not_exist_exactly_as_written()
	{
		var result = Deduplicate("""
			{
			  "paths": { "/a": { "get": { "schema": { "$ref": "#/components/schemas/missing" } } } },
			  "components": { "schemas": { "present_response": { "type": "object", "properties": {} } } }
			}
			""");

		var reference = (string?)result["paths"]!["/a"]!["get"]!["schema"]!["$ref"];

		Assert.Equal("#/components/schemas/missing", reference);
	}
}
