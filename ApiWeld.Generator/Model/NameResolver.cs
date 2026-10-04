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
			var keeper = clash.Where(model => model.Chosen!.FromGet).OrderBy(model => model.ShapeKey, StringComparer.Ordinal).FirstOrDefault();
			var renamed = clash.Where(model => model != keeper).ToList();

			foreach (var model in renamed)
				model.Name = Words.Identifier(model.Chosen!.Qualified);

			diagnostics.Warn($"{clash.Key}: {clash.Count()} different shapes want this name; the ones no GET returns were renamed {string.Join(", ", renamed.Select(model => model.Name).Order(StringComparer.Ordinal))}.");
		}

		foreach (var clash in Clashes(models))
			diagnostics.Error($"{clash.Key}: {clash.Count()} different shapes from {string.Join(", ", clash.Select(model => model.Chosen!.Source).Distinct().Order(StringComparer.Ordinal))} still share this name; add a \"names\" entry for one of their schemas or files.");

		foreach (var model in models.Where(model => model.Name == client))
			diagnostics.Error($"{client}: a generated type would share the client's name; add a \"names\" entry.");
	}

	static List<IGrouping<string, ModelType>> Clashes(IReadOnlyList<ModelType> models)
		=> [.. models.GroupBy(model => model.Name, StringComparer.Ordinal).Where(group => group.Count() > 1).OrderBy(group => group.Key, StringComparer.Ordinal)];
}
