using ApiWeld.Generator;

namespace ApiWeld.Tests.Generator;

public class DescriptionFilesTests : IDisposable
{
	readonly string folder = Directory.CreateTempSubdirectory("apiweld-in").FullName;

	public void Dispose() => Directory.Delete(folder, recursive: true);

	void Touch(string relative)
	{
		var path = Path.Combine(folder, relative);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, "{}");
	}

	[Fact]
	public void Expands_wildcards_and_exact_paths()
	{
		Touch("resources/b.json");
		Touch("resources/a.json");
		Touch("resources/notes.txt");
		Touch("extra/c.json");
		var diagnostics = new List<Diagnostic>();

		var files = DescriptionFiles.Expand(folder, ["resources/*.json", "extra/c.json"], diagnostics);

		Assert.Empty(diagnostics);
		Assert.Equal(new[] { "c.json", "a.json", "b.json" }, files.Select(file => Path.GetFileName(file)));
	}

	[Fact]
	public void Reports_a_pattern_that_matches_nothing()
	{
		var diagnostics = new List<Diagnostic>();

		DescriptionFiles.Expand(folder, ["missing/*.json"], diagnostics);

		Assert.Equal("error: \"missing/*.json\" matched no files.", Assert.Single(diagnostics).ToString());
	}

	[Fact]
	public void Reports_one_file_name_in_two_folders()
	{
		Touch("a/x.json");
		Touch("b/x.json");
		var diagnostics = new List<Diagnostic>();

		DescriptionFiles.Expand(folder, ["a/*.json", "b/*.json"], diagnostics);

		Assert.Contains("x.json", Assert.Single(diagnostics).Message);
	}
}
