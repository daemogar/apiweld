namespace ApiWeld.Generator;

/// <summary>How serious a diagnostic is.</summary>
public enum Severity
{
	/// <summary>Resolved by a rule; generation continues.</summary>
	Warning,

	/// <summary>Cannot be resolved; nothing is written.</summary>
	Error
}

/// <summary>A message about the descriptions or the manifest.</summary>
/// <param name="Severity">Whether generation can continue.</param>
/// <param name="Message">What happened, naming the file and path involved.</param>
public sealed record Diagnostic(Severity Severity, string Message)
{
	/// <inheritdoc/>
	public override string ToString() => (Severity == Severity.Error ? "error: " : "warning: ") + Message;
}

/// <summary>Collects diagnostics while a model is built.</summary>
internal sealed class DiagnosticBag
{
	readonly List<Diagnostic> items = [];

	public IReadOnlyList<Diagnostic> Items => items;

	public bool HasErrors => items.Any(item => item.Severity == Severity.Error);

	public void Warn(string message) => items.Add(new(Severity.Warning, message));

	public void Error(string message) => items.Add(new(Severity.Error, message));
}
