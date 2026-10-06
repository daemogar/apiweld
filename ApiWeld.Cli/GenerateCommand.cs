using ApiWeld.Generator;

namespace ApiWeld.Cli;

/// <summary>The <c>apiweld generate</c> command: manifest in, generated client out.</summary>
public static class GenerateCommand
{
	const string Usage = "usage: apiweld generate <path-to-apiweld.json>";

	/// <summary>Runs the command. Returns the process exit code.</summary>
	public static int Run(string[] args, TextWriter output, TextWriter error)
	{
		if (args is not ["generate", var manifestPath])
		{
			error.WriteLine(Usage);
			return 1;
		}

		if (!File.Exists(manifestPath))
		{
			error.WriteLine($"{manifestPath}: file not found.");
			return 1;
		}

		Manifest manifest;

		try
		{
			manifest = Manifest.Parse(File.ReadAllText(manifestPath));
		}
		catch (ManifestException exception)
		{
			error.WriteLine($"{Path.GetFileName(manifestPath)}: {exception.Message}");
			return 1;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			error.WriteLine($"{Path.GetFileName(manifestPath)}: could not read — {exception.Message}");
			return 1;
		}

		var baseDirectory = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
		var found = new List<Diagnostic>();
		var files = DescriptionFiles.Expand(baseDirectory, manifest.Descriptions, found);

		if (Report(found, error))
			return 1;

		var sources = new List<DescriptionSource>();

		foreach (var file in files)
		{
			try
			{
				sources.Add(new(Path.GetFileName(file), File.ReadAllText(file)));
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				error.WriteLine($"{Path.GetFileName(file)}: could not read — {exception.Message}");
				return 1;
			}
		}

		var result = ClientGenerator.Generate(manifest, sources);

		if (Report(result.Diagnostics, error))
			return 1;

		if (Report(OutputWriter.Write(Path.Combine(baseDirectory, manifest.Output), result.Files), error))
			return 1;

		output.WriteLine($"{manifest.Client}: wrote {result.Files.Count} files to {manifest.Output}.");

		return 0;
	}

	/// <summary>Prints every diagnostic; true when any of them is an error.</summary>
	static bool Report(IEnumerable<Diagnostic> diagnostics, TextWriter error)
	{
		var failed = false;

		foreach (var diagnostic in diagnostics)
		{
			error.WriteLine(diagnostic);
			failed |= diagnostic.Severity == Severity.Error;
		}

		return failed;
	}
}
