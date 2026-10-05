using System.Text;

using ApiWeld.Generator;

namespace ApiWeld.Tests.Generator;

public class OutputWriterTests : IDisposable
{
	readonly string folder = Directory.CreateTempSubdirectory("apiweld-out").FullName;

	public void Dispose() => Directory.Delete(folder, recursive: true);

	static GeneratedFile Generated(string path) => new(path, OutputWriter.Marker + "\nclass A { }\n");

	string At(string relative) => Path.Combine(folder, relative);

	void Put(string relative, string content)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(At(relative))!);
		File.WriteAllText(At(relative), content);
	}

	[Fact]
	public void Writes_each_file_with_a_byte_order_mark()
	{
		Assert.Empty(OutputWriter.Write(folder, [Generated("Models/A.g.cs")]));

		var bytes = File.ReadAllBytes(At("Models/A.g.cs"));

		Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
		Assert.Equal(Generated("Models/A.g.cs").Content, Encoding.UTF8.GetString(bytes[3..]));
	}

	[Fact]
	public void Deletes_stale_generated_files_and_keeps_everything_else()
	{
		Put("Models/Old.g.cs", OutputWriter.Marker + "\n");
		Put("Models/Hand.g.cs", "// mine\n");
		Put("Notes.txt", "keep");

		OutputWriter.Write(folder, [Generated("Models/A.g.cs")]);

		Assert.False(File.Exists(At("Models/Old.g.cs")));
		Assert.True(File.Exists(At("Models/Hand.g.cs")));
		Assert.True(File.Exists(At("Notes.txt")));
	}

	[Fact]
	public void Refuses_to_overwrite_a_file_it_did_not_generate()
	{
		Put("Models/A.g.cs", "// mine\n");

		var diagnostics = OutputWriter.Write(folder, [Generated("Models/A.g.cs"), Generated("Models/B.g.cs")]);

		var error = Assert.Single(diagnostics);
		Assert.Equal(Severity.Error, error.Severity);
		Assert.Contains("A.g.cs", error.Message);
		Assert.Equal("// mine\n", File.ReadAllText(At("Models/A.g.cs")));
		Assert.False(File.Exists(At("Models/B.g.cs")));
	}

	[Fact]
	public void Leaves_an_unchanged_file_untouched()
	{
		OutputWriter.Write(folder, [Generated("A.g.cs")]);
		var stamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
		File.SetLastWriteTimeUtc(At("A.g.cs"), stamp);

		OutputWriter.Write(folder, [Generated("A.g.cs")]);

		Assert.Equal(stamp, File.GetLastWriteTimeUtc(At("A.g.cs")));
	}
}
