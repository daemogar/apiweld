using ApiWeld.Cli;
using ApiWeld.Tests.Generator;

namespace ApiWeld.Tests;

public class GenerateCommandTests : IDisposable
{
	const string Valid = """{ "descriptions": ["resources/*.json"], "namespace": "Example", "client": "ExampleClient", "basePaths": ["/api", "/query"] }""";

	readonly string folder = Directory.CreateTempSubdirectory("apiweld-gen").FullName;

	public void Dispose() => Directory.Delete(folder, recursive: true);

	(int Code, string Out, string Err) Run(params string[] args)
	{
		var output = new StringWriter();
		var error = new StringWriter();
		var code = GenerateCommand.Run(args, output, error);

		return (code, output.ToString(), error.ToString());
	}

	string Manifest(string json)
	{
		var path = Path.Combine(folder, "apiweld.json");
		File.WriteAllText(path, json);

		return path;
	}

	void CopyFixtures()
	{
		Directory.CreateDirectory(Path.Combine(folder, "resources"));

		foreach (var source in EmissionTests.Fixtures())
			File.WriteAllText(Path.Combine(folder, "resources", source.FileName), source.Text);
	}

	List<(string Path, DateTime Written, string Text)> Generated()
		=> [.. Directory.GetFiles(Path.Combine(folder, "Generated"), "*", SearchOption.AllDirectories)
			.Order(StringComparer.Ordinal)
			.Select(path => (path, File.GetLastWriteTimeUtc(path), File.ReadAllText(path)))];

	[Fact]
	public void Generates_the_client_beside_the_manifest()
	{
		CopyFixtures();

		var (code, output, error) = Run("generate", Manifest(Valid));

		Assert.Equal(0, code);
		Assert.StartsWith("ExampleClient: wrote 19 files to Generated.", output);
		Assert.Contains("warning:", error);
		Assert.True(File.Exists(Path.Combine(folder, "Generated", "ExampleClient.g.cs")));
		Assert.True(File.Exists(Path.Combine(folder, "Generated", "Paths", "WidgetsItemParts.g.cs")));
	}

	string Starter => Path.Combine(folder, "Clients", "ExampleClient.cs");

	[Fact]
	public void Writes_a_starter_client_that_compiles_with_the_generated_half()
	{
		CopyFixtures();

		var (code, output, _) = Run("generate", Manifest(Valid));

		Assert.Equal(0, code);
		Assert.EndsWith($"ExampleClient: wrote a starter for its hand-written half to {Path.Combine("Clients", "ExampleClient.cs")}.", output.Trim());

		var sources = Generated().Select(file => (file.Path, file.Text)).Append((Starter, File.ReadAllText(Starter)));
		var (_, diagnostics) = Compiler.Compile(sources);

		Assert.Empty(diagnostics);
	}

	[Fact]
	public void Leaves_an_existing_starter_alone()
	{
		CopyFixtures();
		var manifest = Manifest(Valid);
		Run("generate", manifest);
		File.WriteAllText(Starter, "// edited");

		var (_, output, _) = Run("generate", manifest);

		Assert.Equal("// edited", File.ReadAllText(Starter));
		Assert.DoesNotContain("starter", output);
	}

	[Fact]
	public void Writes_no_starter_when_a_hand_written_part_exists_elsewhere()
	{
		CopyFixtures();
		File.WriteAllText(Path.Combine(folder, "ExampleClient.cs"), "namespace Example;\n\npublic partial class ExampleClient;\n");

		var (code, output, _) = Run("generate", Manifest(Valid));

		Assert.Equal(0, code);
		Assert.DoesNotContain("starter", output);
		Assert.False(Directory.Exists(Path.Combine(folder, "Clients")));
	}

	[Fact]
	public void Ignores_parts_in_build_folders_when_deciding()
	{
		CopyFixtures();
		Directory.CreateDirectory(Path.Combine(folder, "obj"));
		File.WriteAllText(Path.Combine(folder, "obj", "ExampleClient.cs"), "public partial class ExampleClient;\n");

		Run("generate", Manifest(Valid));

		Assert.True(File.Exists(Starter));
	}

	[Fact]
	public void Regenerating_changes_nothing()
	{
		CopyFixtures();
		var manifest = Manifest(Valid);
		Run("generate", manifest);
		var before = Generated();

		Run("generate", manifest);

		Assert.Equal(before, Generated());
	}

	[Fact]
	public void Reports_a_manifest_problem()
	{
		var (code, _, error) = Run("generate", Manifest("""{ "descriptions": ["a.json"], "namespace": "Example" }"""));

		Assert.Equal(1, code);
		Assert.Equal("apiweld.json: \"client\" is required.", error.Trim());
	}

	[Fact]
	public void Prints_usage_without_a_manifest_path()
	{
		var (code, _, error) = Run("generate");

		Assert.Equal(1, code);
		Assert.Equal("usage: apiweld generate <path-to-apiweld.json>", error.Trim());
	}

	[Fact]
	public void Reports_a_missing_manifest()
	{
		var missing = Path.Combine(folder, "nope.json");

		var (code, _, error) = Run("generate", missing);

		Assert.Equal(1, code);
		Assert.Equal($"{missing}: file not found.", error.Trim());
	}

	[Fact]
	public void Reports_a_manifest_it_cannot_read()
	{
		var path = Manifest(Valid);

		using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
		{
			var (code, _, error) = Run("generate", path);

			Assert.Equal(1, code);
			Assert.StartsWith("apiweld.json: could not read — ", error.Trim());
		}
	}

	[Fact]
	public void Reports_a_pattern_that_matches_nothing()
	{
		var (code, _, error) = Run("generate", Manifest(Valid));

		Assert.Equal(1, code);
		Assert.Contains("matched no files", error);
	}
}
