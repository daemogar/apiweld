using ApiWeld.Generator;
using ApiWeld.Generator.Emit;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
	public void Compiles_nested_path_parameters_named_like_the_node_fields()
	{
		var get = Descriptions.Returns("application/json", Descriptions.Thing);
		var description = Descriptions.Document($$"""
			{
				"/api/widgets/{id}/files/{path}": { "get": {{get}} },
				"/api/widgets/{id}/files/{path}/parts/{transport}": { "get": {{get}} }
			}
			""");

		var result = ClientGenerator.Generate(FixtureManifest, [new DescriptionSource("widgets.json", description)]);
		Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics));

		var (_, diagnostics) = Compiler.Compile(result.Files.Select(file => (file.Path, file.Content)).Append(("Consumer.cs", Consumer)));

		Assert.Empty(diagnostics);
	}

	[Theory]
	[InlineData("WidgetsNode", "the navigation class for /widgets")]
	[InlineData("WidgetsV0Operations", "the navigation class for /widgets")]
	[InlineData("ApiTransport", "a type the generated code uses")]
	public void Refuses_a_model_named_like_a_type_the_client_already_has(string name, string what)
	{
		const string get = """
			{ "responses": {
				"200": { "content": { "application/json": { "schema": { "type": "array", "items": { "type": "string" } } } } },
				"400": { "content": { "application/json": { "schema": { "$ref": "#/components/schemas/errors" } } } } } }
			""";
		var description = Descriptions.Document(
			$$"""{ "/api/widgets": { "get": {{get}} } }""",
			"""{ "errors": { "type": "object", "properties": { "message": { "type": "string" } } } }""");
		var manifest = FixtureManifest with { Names = new Dictionary<string, string> { ["errors"] = name } };

		var result = ClientGenerator.Generate(manifest, [new DescriptionSource("widgets.json", description)]);

		Assert.False(result.Succeeded);
		Assert.Contains(result.Diagnostics, d => d.Severity == Severity.Error
			&& d.Message == $"{name}: a model would share its name with {what}; add a \"names\" entry.");
	}

	[Fact]
	public void Lists_every_outside_type_the_generated_code_names()
	{
		var generated = ClientGenerator.Generate(FixtureManifest, Fixtures()).Files;
		var compilation = Compiler.Create(generated.Select(file => (file.Path, file.Content)).Append(("Consumer.cs", Consumer)));

		var named = compilation.SyntaxTrees
			.Where(tree => tree.FilePath != "Consumer.cs")
			.SelectMany(tree =>
			{
				var model = compilation.GetSemanticModel(tree);

				return tree.GetRoot().DescendantNodes().OfType<IdentifierNameSyntax>()
					.Where(name => !name.IsVar)
					.Where(name => model.GetSymbolInfo(name).Symbol is INamedTypeSymbol { Arity: 0 } type
						&& !SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly))
					.Select(name => name.Identifier.ValueText);
			})
			.Distinct()
			.Order(StringComparer.Ordinal)
			.ToList();

		Assert.Contains("ApiTransport", named);
		Assert.Empty(named.Except(ReservedNames.Used));
	}

	[Fact]
	public void Generates_identical_output_every_time()
	{
		var first = ClientGenerator.Generate(FixtureManifest, Fixtures()).Files;
		var second = ClientGenerator.Generate(FixtureManifest, [.. Fixtures().Reverse()]).Files;

		Assert.Equal(first, second);
	}
}
