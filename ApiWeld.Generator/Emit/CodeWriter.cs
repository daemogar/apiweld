using System.Text;

namespace ApiWeld.Generator.Emit;

/// <summary>Builds one generated C# file: the generated-file header, tabs, and LF line endings.</summary>
internal sealed class CodeWriter
{
	static readonly string[] Usings =
	[
		"System", "System.Collections.Generic", "System.Net.Http", "System.Text.Json",
		"System.Text.Json.Serialization", "System.Threading", "System.Threading.Tasks", "ApiWeld.Http"
	];

	readonly StringBuilder text = new();
	int depth;

	public CodeWriter(string @namespace)
	{
		Line(OutputWriter.Marker);
		Line(OutputWriter.Signature);
		Line("#nullable enable");
		Line();

		foreach (var name in Usings)
			Line($"using {name};");

		Line();
		Line($"namespace {@namespace};");
	}

	public CodeWriter Line(string line = "")
	{
		if (line.Length > 0)
			text.Append('\t', depth).Append(line);

		text.Append('\n');

		return this;
	}

	public CodeWriter Open(string line)
	{
		Line(line);
		Line("{");
		depth++;

		return this;
	}

	public CodeWriter Close(string suffix = "")
	{
		depth--;

		return Line("}" + suffix);
	}

	public CodeWriter Summary(string summary) => Line($"/// <summary>{Doc(summary)}</summary>");

	/// <summary>Text made safe for an XML doc comment, on one line.</summary>
	public static string Doc(string value)
		=> string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
			.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

	/// <summary>A C# string literal.</summary>
	public static string Literal(string value)
		=> "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t") + "\"";

	public override string ToString() => text.ToString();
}
