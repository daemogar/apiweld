using System.Text;
using System.Text.RegularExpressions;

namespace ApiWeld.Generator;

/// <summary>Writes a starter hand-written half of the client, once, when the consumer has none yet.</summary>
/// <remarks>See README.md, "The starter client".</remarks>
public static class ClientScaffold
{
	/// <summary>The folder, beside the output folder, that the starter is written to.</summary>
	public const string Folder = "Clients";

	static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

	/// <summary>Writes <c>Clients/{Client}.cs</c> unless a hand-written part of the client exists; returns the path written, or null.</summary>
	public static string? Write(string baseDirectory, Manifest manifest, ICollection<Diagnostic> diagnostics)
	{
		var root = OutputWriter.RootOf(baseDirectory);
		var output = OutputWriter.RootOf(Path.Combine(root, manifest.Output));
		var folder = Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(output))!, Folder);
		var target = Path.Combine(folder, $"{manifest.Client}.cs");

		if (!OutputWriter.IsInside(root, OutputWriter.RootOf(folder)))
			return null;

		try
		{
			if (File.Exists(target) || HandWrittenParts(root, output, manifest.Client).Any())
				return null;

			Directory.CreateDirectory(folder);
			File.WriteAllBytes(target, [.. Utf8WithBom.GetPreamble(), .. Utf8WithBom.GetBytes(Content(manifest))]);

			return Path.GetRelativePath(root, target);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			diagnostics.Add(new(Severity.Warning, $"{target}: could not write the starter client — {exception.Message}"));

			return null;
		}
	}

	/// <summary>The starter's source: the constructor, and commented examples of registration and a hand-written operation.</summary>
	internal static string Content(Manifest manifest)
	{
		var client = manifest.Client;
		string[] lines =
		[
			"using System.Net.Http;",
			"",
			"using ApiWeld.Http;",
			"",
			$"namespace {manifest.Namespace};",
			"",
			$"/// <summary>The hand-written half of {client}; apiweld generate writes the other half.</summary>",
			"/// <param name=\"http\">The HTTP client AddApiWeldClient configures for this client.</param>",
			"/// <param name=\"options\">This client's settings, bound from its configuration section.</param>",
			$"public partial class {client}(HttpClient http, ApiClientOptions<{client}> options)",
			"\t: ApiClient(http, options)",
			"{",
			"\t// Register the client where services are configured:",
			"\t//",
			$"\t//\tservices.AddApiWeldClient<{client}>(configuration.GetSection(\"{client}\"));",
			"\t//",
			"\t// and give that section a base address, in appsettings.json for example:",
			"\t//",
			$"\t//\t\"{client}\": {{ \"BaseUrl\": \"https://api.example.test\" }}",
			"\t//",
			"\t// Hand-written operations go here, or in further parts of this class, and reach the",
			"\t// generated navigation through Api. For example:",
			"\t//",
			"\t//\tpublic Task<WidgetV2Response?> GetWidgetAsync(Guid id, CancellationToken cancellationToken)",
			"\t//\t\t=> Api.Widgets[id].V2.GetAsync(cancellationToken);",
			"\t//",
			"\t// apiweld generate wrote this file once, as a starting point, and never changes it.",
			"}",
		];

		return string.Join("\n", lines) + "\n";
	}

	/// <summary>Hand-written source files beneath <paramref name="root"/> that declare a part of <paramref name="client"/>.</summary>
	static IEnumerable<string> HandWrittenParts(string root, string output, string client)
	{
		var declaration = new Regex($@"\bpartial\s+class\s+{client}\b");

		return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
			.Where(file => !OutputWriter.IsInside(output, file) && !IsBuildOrHidden(root, file))
			.Where(file => declaration.IsMatch(File.ReadAllText(file)));
	}

	/// <summary>Whether <paramref name="file"/> lies in a <c>bin</c>, <c>obj</c> or dot folder beneath <paramref name="root"/>.</summary>
	static bool IsBuildOrHidden(string root, string file)
		=> Path.GetRelativePath(root, Path.GetDirectoryName(file)!)
			.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
			.Any(segment => segment is "bin" or "obj" || segment.StartsWith('.') && segment != ".");
}
