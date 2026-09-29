using ApiWeld.Cli;

namespace ApiWeld.Tests;

public class CliTests : IDisposable
{
	readonly string folder = Directory.CreateTempSubdirectory("apiweld").FullName;

	public void Dispose() => Directory.Delete(folder, recursive: true);

	(int Code, string Out, string Err) Run(params string[] args)
	{
		var output = new StringWriter();
		var error = new StringWriter();
		var code = Normalizer.Run(args, output, error);

		return (code, output.ToString(), error.ToString());
	}

	string Write(string name, string content)
	{
		var path = Path.Combine(folder, name);
		File.WriteAllText(path, content);

		return path;
	}

	[Fact]
	public void Writes_a_modified_document_beside_the_input_when_normalization_changes_it()
	{
		var path = Write("widgets.json", """
			{ "components": { "schemas": { "a": { "type": "string", "format": "guid" } } } }
			""");

		var (code, _, _) = Run("normalize", path);

		Assert.Equal(0, code);

		var produced = Path.Combine(folder, "widgets.modified.json");

		Assert.True(File.Exists(produced));
		Assert.Contains("\"uuid\"", File.ReadAllText(produced));
	}

	[Fact]
	public void Writes_nothing_when_normalization_is_a_no_op()
	{
		var path = Write("widgets.json", """
			{
			  "components": {
			    "schemas": {
			      "a": {
			        "type": "string"
			      }
			    }
			  }
			}
			""");

		var (code, output, _) = Run("normalize", path);

		Assert.Equal(0, code);
		Assert.False(File.Exists(Path.Combine(folder, "widgets.modified.json")));
		Assert.Contains("unchanged", output);
	}

	[Fact]
	public void Indents_with_two_spaces_and_does_not_escape_unnecessarily()
	{
		var path = Write("widgets.json", """
			{ "components": { "schemas": { "a": { "type": "string", "format": "guid", "title": "A & B" } } } }
			""");

		Run("normalize", path);

		var produced = File.ReadAllText(Path.Combine(folder, "widgets.modified.json"));

		Assert.Contains("\n  \"components\"", produced.ReplaceLineEndings("\n"));
		Assert.Contains("A & B", produced);
	}

	// Review Focus 5.
	[Fact]
	public void Reports_a_missing_file_without_throwing()
	{
		var (code, _, error) = Run("normalize", Path.Combine(folder, "absent.json"));

		Assert.NotEqual(0, code);
		Assert.Contains("absent.json", error);
	}

	[Fact]
	public void Reports_a_file_that_is_not_json_without_throwing()
	{
		var path = Write("widgets.txt", "{}");

		var (code, _, error) = Run("normalize", path);

		Assert.NotEqual(0, code);
		Assert.Contains(".json", error);
	}

	[Fact]
	public void Reports_malformed_json_naming_the_file_without_throwing()
	{
		var path = Write("widgets.json", "{ not json");

		var (code, _, error) = Run("normalize", path);

		Assert.NotEqual(0, code);
		Assert.Contains("widgets.json", error);
	}

	[Fact]
	public void Reports_usage_when_given_no_arguments()
	{
		var (code, _, error) = Run();

		Assert.NotEqual(0, code);
		Assert.Contains("usage", error, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void Reports_usage_for_an_unknown_command()
	{
		var path = Write("widgets.json", "{}");

		var (code, _, error) = Run("frobnicate", path);

		Assert.NotEqual(0, code);
		Assert.Contains("usage", error, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void Writes_lf_line_endings_with_one_trailing_newline()
	{
		var path = Write("widgets.json", """
			{ "components": { "schemas": { "a": { "type": "string", "format": "guid" } } } }
			""");

		Run("normalize", path);

		var produced = File.ReadAllText(Path.Combine(folder, "widgets.modified.json"));

		Assert.DoesNotContain('\r', produced);
		Assert.EndsWith("}\n", produced);
		Assert.DoesNotContain("\n\n", produced);
	}
}
