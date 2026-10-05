using System.Reflection;
using System.Runtime.Loader;

using ApiWeld.Http;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ApiWeld.Tests.Generator;

/// <summary>Compiles generated sources in memory against the runtime package, reporting every error and warning.</summary>
static class Compiler
{
	static readonly MetadataReference[] References = [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
		.Split(Path.PathSeparator)
		.Append(typeof(ApiTransport).Assembly.Location)
		.Distinct(StringComparer.OrdinalIgnoreCase)
		.Select(path => MetadataReference.CreateFromFile(path))];

	public static (Assembly? Assembly, IReadOnlyList<string> Diagnostics) Compile(IEnumerable<(string Path, string Source)> sources)
	{
		var parse = new CSharpParseOptions(LanguageVersion.CSharp14, DocumentationMode.Diagnose);
		var compilation = CSharpCompilation.Create(
			"Generated" + Guid.NewGuid().ToString("N"),
			sources.Select(source => CSharpSyntaxTree.ParseText(source.Source, parse, source.Path)),
			References,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, warningLevel: 9999));

		using var image = new MemoryStream();
		var result = compilation.Emit(image, xmlDocumentationStream: new MemoryStream());
		var diagnostics = result.Diagnostics
			.Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
			.Select(diagnostic => diagnostic.ToString())
			.ToList();

		if (!result.Success)
			return (null, diagnostics);

		image.Position = 0;

		return (AssemblyLoadContext.Default.LoadFromStream(image), diagnostics);
	}
}
