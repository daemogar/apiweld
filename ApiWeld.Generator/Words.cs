using System.Text.RegularExpressions;

namespace ApiWeld.Generator;

/// <summary>Turns description text into C# identifiers.</summary>
internal static partial class Words
{
	[GeneratedRegex(@"[A-Z]+(?![a-z])|[A-Z]?[a-z]+|\d+")]
	private static partial Regex Word();

	static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
	{
		"abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
		"continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern",
		"false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface",
		"internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override",
		"params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
		"sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof",
		"uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while"
	};

	public static IReadOnlyList<string> Split(string text) => [.. Word().Matches(text).Select(match => match.Value)];

	public static string Pascal(string text) => string.Concat(Split(text).Select(Capitalize));

	public static string Camel(string text)
	{
		var pascal = Pascal(text);

		return pascal.Length == 0 ? pascal : char.ToLowerInvariant(pascal[0]) + pascal[1..];
	}

	/// <summary>PascalCase with the last word made singular: <c>academic-disciplines</c> becomes <c>AcademicDiscipline</c>.</summary>
	public static string SingularPascal(string text)
	{
		var words = Split(text).Select(Capitalize).ToList();

		if (words.Count > 0)
			words[^1] = Singular(words[^1]);

		return string.Concat(words);
	}

	public static string Singular(string word)
	{
		var lower = word.ToLowerInvariant();

		if (lower.Length > 3 && lower.EndsWith("ies", StringComparison.Ordinal))
			return word[..^3] + "y";

		if (lower.EndsWith("sses", StringComparison.Ordinal) || lower.EndsWith("xes", StringComparison.Ordinal)
			|| lower.EndsWith("ches", StringComparison.Ordinal) || lower.EndsWith("shes", StringComparison.Ordinal)
			|| lower.EndsWith("uses", StringComparison.Ordinal))
			return word[..^2];

		if (lower.Length > 1 && lower.EndsWith('s') && !lower.EndsWith("ss", StringComparison.Ordinal)
			&& !lower.EndsWith("us", StringComparison.Ordinal) && !lower.EndsWith("is", StringComparison.Ordinal))
			return word[..^1];

		return word;
	}

	/// <summary>A valid identifier: a leading digit gets <c>_</c>, a keyword gets <c>@</c>, empty text becomes <paramref name="fallback"/>.</summary>
	public static string Identifier(string candidate, string fallback = "Value")
	{
		if (candidate.Length == 0)
			return fallback;

		if (char.IsDigit(candidate[0]))
			return "_" + candidate;

		return Keywords.Contains(candidate) ? "@" + candidate : candidate;
	}

	/// <summary>Takes <paramref name="name"/>, or the first of <c>name2</c>, <c>name3</c>, … not already taken.</summary>
	public static string Unique(string name, ISet<string> taken)
	{
		var candidate = name;

		for (var number = 2; !taken.Add(candidate); number++)
			candidate = name + number;

		return candidate;
	}

	static string Capitalize(string word) => word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..];
}
