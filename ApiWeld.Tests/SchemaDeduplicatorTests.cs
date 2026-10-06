using System.Text.Json.Nodes;

using ApiWeld.Core;

namespace ApiWeld.Tests;

public class SchemaDeduplicatorTests
{
	static JsonObject Deduplicate(string json, string resource = "widgets")
		=> SchemaDeduplicator.Deduplicate((JsonObject)JsonNode.Parse(json)!, resource);

	static JsonObject Schemas(JsonObject document) => (JsonObject)document["components"]!["schemas"]!;

	const string TwoIdenticalResponses = """
		{
		  "paths": {
		    "/widgets": {
		      "get": { "responses": { "200": { "content": { "application/json": { "schema": { "$ref": "#/components/schemas/widgets_get_response" } } } } } },
		      "put": { "responses": { "200": { "content": { "application/json": { "schema": { "$ref": "#/components/schemas/widgets_put_response" } } } } } }
		    }
		  },
		  "components": {
		    "schemas": {
		      "widgets_put_response": { "type": "object", "properties": { "id": { "type": "string" } } },
		      "widgets_get_response": { "type": "object", "properties": { "id": { "type": "string" } } }
		    }
		  }
		}
		""";

	[Fact]
	public void Folds_identical_schemas_to_one_and_rewrites_references_to_it()
	{
		var result = Deduplicate(TwoIdenticalResponses);

		Assert.True(Schemas(result).ContainsKey("widgets_get_response"));
		Assert.False(Schemas(result).ContainsKey("widgets_put_response"));

		var put = (string?)result["paths"]!["/widgets"]!["put"]!["responses"]!["200"]!["content"]!["application/json"]!["schema"]!["$ref"];

		Assert.Equal("#/components/schemas/widgets_get_response", put);
	}

	[Fact]
	public void Prefers_the_resources_own_get_response_as_the_survivor()
	{
		var result = Deduplicate(TwoIdenticalResponses);

		Assert.True(Schemas(result).ContainsKey("widgets_get_response"));
	}

	[Fact]
	public void Keeps_the_first_schema_when_none_is_the_get_response()
	{
		var result = Deduplicate(TwoIdenticalResponses, resource: "other");

		Assert.True(Schemas(result).ContainsKey("widgets_put_response"));
		Assert.False(Schemas(result).ContainsKey("widgets_get_response"));
	}

	[Fact]
	public void Never_folds_a_request_onto_an_identical_response()
	{
		var result = Deduplicate("""
			{
			  "paths": {
			    "/widgets": {
			      "get": { "responses": { "200": { "content": { "application/json": { "schema": { "$ref": "#/components/schemas/widgets_get_response" } } } } } },
			      "post": { "requestBody": { "content": { "application/json": { "schema": { "$ref": "#/components/schemas/widgets_post_request" } } } } }
			    }
			  },
			  "components": {
			    "schemas": {
			      "widgets_get_response": { "type": "object", "properties": { "id": { "type": "string" } } },
			      "widgets_post_request": { "type": "object", "properties": { "id": { "type": "string" } } }
			    }
			  }
			}
			""");

		Assert.Equal(2, Schemas(result).Count);
	}

	[Fact]
	public void Folds_two_identical_requests()
	{
		var result = Deduplicate("""
			{
			  "paths": {
			    "/widgets": {
			      "post": { "requestBody": { "content": { "application/json": { "schema": { "$ref": "#/components/schemas/widgets_post_request" } } } } },
			      "put": { "requestBody": { "content": { "application/json": { "schema": { "$ref": "#/components/schemas/widgets_put_request" } } } } }
			    }
			  },
			  "components": {
			    "schemas": {
			      "widgets_post_request": { "type": "object", "properties": { "id": { "type": "string" } } },
			      "widgets_put_request": { "type": "object", "properties": { "id": { "type": "string" } } }
			    }
			  }
			}
			""");

		Assert.Equal(["widgets_post_request"], Schemas(result).Select(p => p.Key));
	}

	// A nested schema takes the direction of every root that reaches it.
	[Fact]
	public void Never_folds_a_nested_request_schema_onto_an_identical_nested_response_schema()
	{
		var result = Deduplicate("""
			{
			  "paths": {
			    "/widgets": {
			      "get": { "responses": { "200": { "content": { "application/json": { "schema": { "$ref": "#/components/schemas/outer_response" } } } } } },
			      "post": { "requestBody": { "content": { "application/json": { "schema": { "$ref": "#/components/schemas/outer_request" } } } } }
			    }
			  },
			  "components": {
			    "schemas": {
			      "outer_response": { "type": "object", "properties": { "inner": { "$ref": "#/components/schemas/inner_a" }, "kind": { "type": "string" } } },
			      "outer_request": { "type": "object", "properties": { "inner": { "$ref": "#/components/schemas/inner_b" } } },
			      "inner_a": { "type": "object", "properties": { "id": { "type": "string" } } },
			      "inner_b": { "type": "object", "properties": { "id": { "type": "string" } } }
			    }
			  }
			}
			""");

		Assert.True(Schemas(result).ContainsKey("inner_a"));
		Assert.True(Schemas(result).ContainsKey("inner_b"));
	}

	[Fact]
	public void Reads_the_direction_through_a_shared_request_body_and_response()
	{
		var result = Deduplicate("""
			{
			  "paths": {
			    "/widgets": {
			      "get": { "responses": { "200": { "$ref": "#/components/responses/Widget" } } },
			      "post": { "requestBody": { "$ref": "#/components/requestBodies/Widget" } }
			    }
			  },
			  "components": {
			    "responses": { "Widget": { "content": { "application/json": { "schema": { "$ref": "#/components/schemas/a" } } } } },
			    "requestBodies": { "Widget": { "content": { "application/json": { "schema": { "$ref": "#/components/schemas/b" } } } } },
			    "schemas": {
			      "a": { "type": "object", "properties": { "id": { "type": "string" } } },
			      "b": { "type": "object", "properties": { "id": { "type": "string" } } }
			    }
			  }
			}
			""");

		Assert.Equal(2, Schemas(result).Count);
	}

	// The rule that stops a live type being renamed after a dead one.
	[Fact]
	public void Leaves_an_unreferenced_duplicate_alone()
	{
		var result = Deduplicate("""
			{
			  "paths": { "/a": { "get": { "responses": { "200": { "content": { "application/json": { "schema": { "$ref": "#/components/schemas/live_response" } } } } } } } },
			  "components": {
			    "schemas": {
			      "live_response": { "type": "object", "properties": { "id": { "type": "string" } } },
			      "orphan": { "type": "object", "properties": { "id": { "type": "string" } } }
			    }
			  }
			}
			""");

		Assert.True(Schemas(result).ContainsKey("live_response"));
		Assert.True(Schemas(result).ContainsKey("orphan"));
	}

	[Fact]
	public void Does_not_merge_two_schemas_that_are_merely_similar()
	{
		var result = Deduplicate("""
			{
			  "paths": {
			    "/a": { "get": { "responses": { "200": { "content": { "application/json": { "schema": { "$ref": "#/components/schemas/a_response" } } } } } } },
			    "/b": { "get": { "responses": { "200": { "content": { "application/json": { "schema": { "$ref": "#/components/schemas/b_response" } } } } } } }
			  },
			  "components": {
			    "schemas": {
			      "a_response": { "type": "object", "properties": { "id": { "type": "string" } } },
			      "b_response": { "type": "object", "properties": { "id": { "type": "integer" } } }
			    }
			  }
			}
			""");

		Assert.Equal(2, Schemas(result).Count);
	}

	[Fact]
	public void Follows_references_made_from_one_schema_to_another()
	{
		var result = Deduplicate("""
			{
			  "paths": { "/a": { "get": { "responses": { "200": { "content": { "application/json": { "schema": { "$ref": "#/components/schemas/outer_response" } } } } } } } },
			  "components": {
			    "schemas": {
			      "outer_response": { "type": "object", "properties": { "inner": { "$ref": "#/components/schemas/inner_response" } } },
			      "inner_response": { "type": "object", "properties": { "id": { "type": "string" } } },
			      "inner_request": { "type": "object", "properties": { "id": { "type": "string" } } }
			    }
			  }
			}
			""");

		// inner_request is unreferenced, so it survives untouched rather than folding.
		Assert.True(Schemas(result).ContainsKey("inner_response"));
		Assert.True(Schemas(result).ContainsKey("inner_request"));
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
			  "paths": { "/a": { "get": { "responses": { "200": { "content": { "application/json": { "schema": { "$ref": "#/components/schemas/missing" } } } } } } } },
			  "components": { "schemas": { "present_response": { "type": "object", "properties": {} } } }
			}
			""");

		var reference = (string?)result["paths"]!["/a"]!["get"]!["responses"]!["200"]!["content"]!["application/json"]!["schema"]!["$ref"];

		Assert.Equal("#/components/schemas/missing", reference);
	}
}
