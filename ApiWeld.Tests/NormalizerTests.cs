using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

using ApiWeld.Core;

namespace ApiWeld.Tests;

public class NormalizerTests
{
	static string Fixture(string name)
	{
		using var stream = Assembly.GetExecutingAssembly()
			.GetManifestResourceStream($"ApiWeld.Tests.Fixtures.{name}")
			?? throw new InvalidOperationException($"No embedded fixture named {name}.");

		using var reader = new StreamReader(stream);

		return reader.ReadToEnd();
	}

	static string Render(JsonNode node)
		=> node.ToJsonString(new()
		{
			WriteIndented = true,
			NewLine = "\n",
			Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
		});

	[Fact]
	public Task Normalizes_a_composite_description()
	{
		var result = OpenApiNormalizer.Normalize(Fixture("composite.json"), "widgets");

		return Verify(Render(result)).UseDirectory("Snapshots");
	}

	[Fact]
	public void Normalizing_an_already_normalized_document_changes_nothing()
	{
		var once = OpenApiNormalizer.Normalize(Fixture("composite.json"), "widgets");
		var twice = OpenApiNormalizer.Normalize(Render(once), "widgets");

		Assert.True(JsonNode.DeepEquals(once, twice));
	}

	[Fact]
	public void Applies_format_substitution_before_parsing()
	{
		var result = OpenApiNormalizer.Normalize(Fixture("composite.json"), "widgets");
		var id = result["components"]!["schemas"]!["widgets_get_response"]!["properties"]!["id"]!;

		Assert.Equal("uuid", (string?)id["format"]);
	}

	[Fact]
	public void Collapses_before_it_deduplicates()
	{
		// The two schemas are NOT identical as written — the response's changedOn
		// carries an extra absent branch. They become identical only after the
		// collapse, so deduplicating first would find nothing to fold and both
		// would survive. This test fails if the order is ever reversed.
		var result = OpenApiNormalizer.Normalize(Fixture("composite.json"), "widgets");
		var schemas = (JsonObject)result["components"]!["schemas"]!;

		Assert.True(schemas.ContainsKey("widgets_get_response"));
		Assert.False(schemas.ContainsKey("widgets_post_request"));
	}

	[Fact]
	public void Rejects_a_description_whose_root_is_not_an_object()
	{
		Assert.Throws<JsonException>(() => OpenApiNormalizer.Normalize("[1, 2]", "widgets"));
	}
}
