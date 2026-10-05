using ApiWeld.Generator;

namespace ApiWeld.Tests.Generator;

public class ManifestTests
{
	[Fact]
	public void Reads_every_setting()
	{
		var manifest = Manifest.Parse("""
			{ "descriptions": ["resources/*.json"], "namespace": "Example.Api", "client": "ExampleClient", "output": "Out",
			  "basePaths": ["api/", "/query"], "paging": { "offset": "skip", "limit": "take", "totalHeader": "X-Count" },
			  "names": { "errors_1_0_0": "Errors" } }
			""");

		Assert.Equal(new[] { "resources/*.json" }, manifest.Descriptions);
		Assert.Equal("Example.Api", manifest.Namespace);
		Assert.Equal("ExampleClient", manifest.Client);
		Assert.Equal("Out", manifest.Output);
		Assert.Equal(new[] { "/api", "/query" }, manifest.BasePaths);
		Assert.Equal(new PagingConvention("skip", "take", "X-Count"), manifest.Paging);
		Assert.Equal("Errors", manifest.Names["errors_1_0_0"]);
	}

	[Fact]
	public void Fills_in_defaults()
	{
		var manifest = Manifest.Parse("""{ "descriptions": ["a.json"], "namespace": "Example", "client": "ExampleClient" }""");

		Assert.Equal("Generated", manifest.Output);
		Assert.Empty(manifest.BasePaths);
		Assert.Equal(new PagingConvention(), manifest.Paging);
		Assert.Empty(manifest.Names);
	}

	[Theory]
	[InlineData("""{ "namespace": "Example", "client": "C" }""", "\"descriptions\" is required.")]
	[InlineData("""{ "descriptions": ["a.json"], "client": "C" }""", "\"namespace\" is required.")]
	[InlineData("""{ "descriptions": ["a.json"], "namespace": "Example" }""", "\"client\" is required.")]
	[InlineData("""{ "descriptions": [], "namespace": "Example", "client": "C" }""", "\"descriptions\" lists no files.")]
	[InlineData("""{ "descriptions": ["a.json"], "namespace": "Example..Api", "client": "C" }""", "\"namespace\" is not a valid C# namespace: Example..Api")]
	[InlineData("""{ "descriptions": ["a.json"], "namespace": "Example", "client": "2C" }""", "\"client\" is not a valid C# identifier: 2C")]
	[InlineData("""{ "descriptions": ["a.json"], "namespace": "Example", "client": "C", "names": { "x": 3 } }""", "\"names\".\"x\" must be a valid C# identifier.")]
	[InlineData("""{ "descriptions": "a.json", "namespace": "Example", "client": "C" }""", "\"descriptions\" must be an array of strings.")]
	[InlineData("""{ "descriptions": ["a.json"], "namespace": "Example", "client": "class" }""", "\"client\" is not a valid C# identifier: class")]
	[InlineData("""{ "descriptions": ["a.json"], "namespace": "Example.namespace", "client": "C" }""", "\"namespace\" is not a valid C# namespace: Example.namespace")]
	[InlineData("""{ "descriptions": ["a.json"], "namespace": "Example", "client": "C", "names": { "x": "int" } }""", "\"names\".\"x\" must be a valid C# identifier.")]
	[InlineData("""{ "descriptions": ["a.json"], "namespace": "Example", "client": "C", "paging": "skip" }""", "\"paging\" must be an object.")]
	[InlineData("""{ "descriptions": ["a.json"], "namespace": "Example", "client": "C", "names": ["a"] }""", "\"names\" must be an object.")]
	[InlineData("""{ "descriptions": ["a.json"], "namespace": "Example", "client": "C", "basepaths": ["/api"] }""", "unknown key \"basepaths\".")]
	[InlineData("""{ "descriptions": ["a.json"], "namespace": "Example", "client": "C", "paging": { "skip": "s" } }""", "unknown key \"paging\".\"skip\".")]
	[InlineData("""{ "descriptions": ["a.json"], "namespace": "Example", "client": "C", "singulars": { "octopi": "sea octopus" } }""", "\"singulars\".\"octopi\" must map one word to one word.")]
	[InlineData("""{ "descriptions": ["a.json"], "namespace": "Example", "client": "C", "singulars": { "octopi": 3 } }""", "\"singulars\".\"octopi\" must map one word to one word.")]
	[InlineData("""[ ]""", "the manifest must be a JSON object.")]
	public void Names_the_first_problem(string json, string message)
	{
		Assert.Equal(message, Assert.Throws<ManifestException>(() => Manifest.Parse(json)).Message);
	}

	[Fact]
	public void Allows_keys_starting_with_a_dollar_sign()
	{
		var manifest = Manifest.Parse("""{ "$schema": "https://example.test/apiweld.schema.json", "descriptions": ["a.json"], "namespace": "Example", "client": "C" }""");

		Assert.Equal("C", manifest.Client);
	}

	[Fact]
	public void Reads_singulars_keyed_by_the_lowercase_plural()
	{
		var manifest = Manifest.Parse("""{ "descriptions": ["a.json"], "namespace": "Example", "client": "C", "singulars": { "Octopi": "octopus" } }""");

		Assert.Equal("octopus", manifest.Singulars["octopi"]);
	}

	[Fact]
	public void Rejects_text_that_is_not_json()
	{
		Assert.StartsWith("not valid JSON", Assert.Throws<ManifestException>(() => Manifest.Parse("{ nope")).Message);
	}
}
