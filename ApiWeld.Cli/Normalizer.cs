using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

using ApiWeld.Core;

namespace ApiWeld.Cli;

/// <summary>The file layer: everything that reads, compares or writes.</summary>
public static class Normalizer
{
	const string Usage = "usage: apiweld normalize <path-to-description.json>";

	// Mirrors the message OpenApiNormalizer throws for a non-object root, so a genuine
	// JSON syntax error and a validly-parsed but wrongly-shaped root report differently.
	const string RootNotObjectMessage = "The description's root is not a JSON object.";

	/// <summary>Pinned to LF with a trailing newline so identical input writes identical bytes on every OS.</summary>
	static readonly JsonSerializerOptions Output = new()
	{
		WriteIndented = true,
		NewLine = "\n",
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	/// <summary>Runs one command. Returns the process exit code.</summary>
	public static int Run(string[] args, TextWriter output, TextWriter error)
	{
		if (args is not ["normalize", var path])
		{
			error.WriteLine(Usage);
			return 1;
		}

		if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
		{
			error.WriteLine($"{path}: expected a .json description.");
			return 1;
		}

		if (!File.Exists(path))
		{
			error.WriteLine($"{path}: file not found.");
			return 1;
		}

		var file = new FileInfo(path);
		var name = Path.GetFileNameWithoutExtension(file.Name);

		string text;

		try
		{
			text = File.ReadAllText(file.FullName);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			error.WriteLine($"{file.Name}: could not read — {exception.Message}");
			return 1;
		}

		JsonObject normalized;

		try
		{
			normalized = OpenApiNormalizer.Normalize(text, name);
		}
		catch (JsonException exception)
		{
			var prefix = exception.Message == RootNotObjectMessage ? "cannot normalize" : "not valid JSON";
			error.WriteLine($"{file.Name}: {prefix} — {exception.Message}");
			return 1;
		}
		catch (Exception exception)
		{
			error.WriteLine($"{file.Name}: could not normalize — {exception.Message}");
			return 1;
		}

		var target = Path.Combine(file.DirectoryName!, $"{name}.modified.json");

		// Compared against the document as written, not against the format-mapped text,
		// so a substitution alone is still a change worth writing.
		if (JsonNode.DeepEquals(JsonNode.Parse(text), normalized))
		{
			output.WriteLine($"{name}: unchanged — nothing written.");

			if (File.Exists(target))
				error.WriteLine($"{Path.GetFileName(target)}: stale — {name}.json now normalizes to itself");

			return 0;
		}

		try
		{
			File.WriteAllText(target, normalized.ToJsonString(Output) + "\n");
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			error.WriteLine($"{Path.GetFileName(target)}: could not write — {exception.Message}");
			return 1;
		}

		output.WriteLine($"{name}: wrote {Path.GetFileName(target)}.");

		return 0;
	}
}
