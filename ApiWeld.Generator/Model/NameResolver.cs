namespace ApiWeld.Generator.Model;

/// <summary>Gives every model its final name.</summary>
/// <remarks>See README.md, "Names".</remarks>
internal static class NameResolver
{
	public static void Resolve(IReadOnlyList<ModelType> models, string client, DiagnosticBag diagnostics)
	{
		foreach (var model in models)
		{
			model.Chosen = model.Candidates
				.OrderBy(candidate => candidate.Plain.Length)
				.ThenBy(candidate => candidate.Plain, StringComparer.Ordinal)
				.First();
			model.Name = Words.Identifier(model.Chosen.Plain);
		}

		foreach (var clash in Clashes(models))
		{
			var keeper = clash.Where(ReachedByGet).OrderBy(model => model.ShapeKey, StringComparer.Ordinal).FirstOrDefault();
			var renamed = clash.Where(model => model != keeper).ToList();

			foreach (var model in renamed)
				model.Name = Words.Identifier(model.Chosen!.Qualified);

			diagnostics.Warn($"{clash.Key}: {clash.Count()} different shapes want this name; kept by {(keeper is null ? "none" : "the shape a GET returns")}, renamed {string.Join(", ", renamed.Select(model => model.Name).Order(StringComparer.Ordinal))}.");
		}

		foreach (var clash in Clashes(models))
		{
			var sources = clash.SelectMany(model => model.Candidates).Select(candidate => candidate.Source).Distinct().Order(StringComparer.Ordinal).ToList();
			var keys = sources.Select(Path.GetFileNameWithoutExtension).Distinct().Order(StringComparer.Ordinal);

			diagnostics.Error($"{clash.Key}: {clash.Count()} different shapes from {string.Join(", ", sources)} still share this name; add a \"names\" entry for a file stem ({string.Join(", ", keys)}) or one of its component schema names.");
		}

		foreach (var model in models.Where(model => model.Name == client))
			diagnostics.Error($"{client}: a generated type would share the client's name; add a \"names\" entry.");
	}

	static bool ReachedByGet(ModelType model)
		=> model.Candidates.Any(candidate => candidate.FromGet && candidate.Plain == model.Chosen!.Plain);

	static List<IGrouping<string, ModelType>> Clashes(IReadOnlyList<ModelType> models)
		=> [.. models.GroupBy(model => model.Name, StringComparer.Ordinal).Where(group => group.Count() > 1).OrderBy(group => group.Key, StringComparer.Ordinal)];
}
