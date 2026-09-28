namespace ApiWeld.Core;

/// <summary>Canonical spellings for formats a description may use non-standard names for.</summary>
/// <remarks>Why this is text rather than a tree walk: see CONTRIBUTING.md, "Format substitution is text replacement".</remarks>
public static class FormatMap
{
	const string Pattern = "\"format\": \"{0}\"";

	static readonly (string From, string To)[] Mappings =
	[
		("guid", "uuid"),
		("email", "string"),
	];

	/// <summary>Every recognized format replaced by its canonical spelling.</summary>
	public static string Apply(string documentText)
	{
		foreach (var (from, to) in Mappings)
			documentText = documentText.Replace(
				string.Format(Pattern, from),
				string.Format(Pattern, to));

		return documentText;
	}
}
