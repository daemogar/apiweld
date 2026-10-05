namespace ApiWeld.Generator;

/// <summary>Finds the description files a manifest lists.</summary>
public static class DescriptionFiles
{
	/// <summary>Every file the patterns match, as full paths in ordinal order; problems are added to <paramref name="diagnostics"/>.</summary>
	public static IReadOnlyList<string> Expand(string baseDirectory, IEnumerable<string> patterns, ICollection<Diagnostic> diagnostics)
	{
		var found = new SortedSet<string>(StringComparer.Ordinal);

		foreach (var pattern in patterns)
		{
			var full = Path.GetFullPath(Path.Combine(baseDirectory, pattern));
			var directory = Path.GetDirectoryName(full)!;
			var name = Path.GetFileName(full);
			string[] matches;

			if (name.IndexOfAny(['*', '?']) >= 0)
				matches = Directory.Exists(directory) ? Directory.GetFiles(directory, name) : [];
			else
				matches = File.Exists(full) ? [full] : [];

			if (matches.Length == 0)
				diagnostics.Add(new(Severity.Error, $"\"{pattern}\" matched no files."));

			found.UnionWith(matches);
		}

		foreach (var clash in found.GroupBy(file => Path.GetFileName(file), StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
			diagnostics.Add(new(Severity.Error, $"{clash.Key}: found in more than one folder; descriptions are named by file, so each name must be unique."));

		return [.. found];
	}
}
