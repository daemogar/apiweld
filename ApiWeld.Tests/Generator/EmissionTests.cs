using ApiWeld.Generator;

namespace ApiWeld.Tests.Generator;

public class EmissionTests
{
	internal static readonly Manifest FixtureManifest = new()
	{
		Descriptions = ["*.json"],
		Namespace = "Example",
		Client = "ExampleClient",
		BasePaths = ["/api", "/query"]
	};

	/// <summary>The consumer's half of the client, which a real project writes by hand.</summary>
	internal const string Consumer = """
		using System;
		using System.Net.Http;
		using ApiWeld.Http;

		namespace Example;

		/// <summary>The hand-written half.</summary>
		public partial class ExampleClient
		{
			/// <summary>Creates the client over <paramref name="http"/>.</summary>
			public ExampleClient(HttpClient http) : base(http, new ApiClientOptions()) { }

			/// <summary>The generated navigation root.</summary>
			public ExampleClientApi Paths => Api;
		}
		""";

	internal static IReadOnlyList<DescriptionSource> Fixtures()
	{
		const string prefix = "ApiWeld.Tests.Fixtures.Generator.";
		var assembly = typeof(EmissionTests).Assembly;

		return [.. assembly.GetManifestResourceNames()
			.Where(name => name.StartsWith(prefix, StringComparison.Ordinal))
			.Order(StringComparer.Ordinal)
			.Select(name =>
			{
				using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);

				return new DescriptionSource(name[prefix.Length..], reader.ReadToEnd());
			})];
	}

	[Fact]
	public Task Generates_a_client_from_several_descriptions()
	{
		var result = ClientGenerator.Generate(FixtureManifest, Fixtures());

		Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics));

		var text = string.Join("\n", result.Diagnostics) + "\n\n"
			+ string.Join("\n", result.Files.Select(file => $"==== {file.Path} ====\n{file.Content}"));

		return Verify(text).UseDirectory("Snapshots");
	}

	[Fact]
	public void The_generated_client_compiles_without_errors_or_warnings()
	{
		var result = ClientGenerator.Generate(FixtureManifest, Fixtures());

		var (_, diagnostics) = Compiler.Compile(result.Files.Select(file => (file.Path, file.Content)).Append(("Consumer.cs", Consumer)));

		Assert.Empty(diagnostics);
	}

	[Fact]
	public void Generates_identical_output_every_time()
	{
		var first = ClientGenerator.Generate(FixtureManifest, Fixtures()).Files;
		var second = ClientGenerator.Generate(FixtureManifest, [.. Fixtures().Reverse()]).Files;

		Assert.Equal(first, second);
	}
}
