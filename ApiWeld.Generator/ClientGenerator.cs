using ApiWeld.Generator.Emit;
using ApiWeld.Generator.Model;

namespace ApiWeld.Generator;

/// <summary>What generation produced, and everything it had to say.</summary>
/// <param name="Files">The generated files, sorted by path; empty when any diagnostic is an error.</param>
/// <param name="Diagnostics">Warnings and errors, in the order they were found.</param>
public sealed record GenerationResult(IReadOnlyList<GeneratedFile> Files, IReadOnlyList<Diagnostic> Diagnostics)
{
	/// <summary>Whether no diagnostic is an error.</summary>
	public bool Succeeded => Diagnostics.All(diagnostic => diagnostic.Severity != Severity.Error);
}

/// <summary>Turns a set of descriptions into the files of one client.</summary>
public static class ClientGenerator
{
	/// <summary>Builds the model, names its types and writes every file, or reports why it cannot.</summary>
	public static GenerationResult Generate(Manifest manifest, IReadOnlyList<DescriptionSource> sources)
	{
		var diagnostics = new DiagnosticBag();
		var model = ApiModelBuilder.Build(manifest, sources, diagnostics);

		if (!diagnostics.HasErrors)
			NameResolver.Resolve(model.Models, manifest.Client, diagnostics);

		if (diagnostics.HasErrors)
			return new([], diagnostics.Items);

		var files = new List<GeneratedFile> { RootEmitter.Emit(model.Root, manifest) };
		files.AddRange(model.Root.Descendants().Select(node => PathEmitter.Emit(node, manifest)));
		files.AddRange(model.Models.Select(type => ModelEmitter.Emit(type, manifest.Namespace)));

		foreach (var clash in files.GroupBy(file => file.Path, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
			diagnostics.Error($"{string.Join(" and ", clash.Select(file => file.Path))} differ only in case, which not every file system can hold.");

		return diagnostics.HasErrors
			? new([], diagnostics.Items)
			: new([.. files.OrderBy(file => file.Path, StringComparer.Ordinal)], diagnostics.Items);
	}
}
