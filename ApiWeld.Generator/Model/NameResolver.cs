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
				.ThenBy(candidate => candidate.Root, StringComparer.Ordinal)
				.ThenBy(candidate => candidate.Route, StringComparer.Ordinal)
				.First();
			model.Name = Words.Identifier(model.Chosen.Plain);
		}

		var kept = new HashSet<ModelType>();
		var origin = new Dictionary<ModelType, List<ModelType>>();

		foreach (var clash in Clashes(models))
		{
			var keeper = Keeper(clash);
			var renamed = clash.Where(model => model != keeper).ToList();

			if (keeper is not null)
				kept.Add(keeper);

			foreach (var model in renamed)
				origin[model] = [.. clash];

			foreach (var model in renamed)
				model.Name = Words.Identifier(model.Chosen!.Qualified);

			diagnostics.Warn($"{clash.Key}: {clash.Count()} different shapes want this name; kept by {KeptBy(keeper)}, renamed {string.Join(", ", renamed.Select(model => model.Name).Order(StringComparer.Ordinal))}.");
		}

		foreach (var clash in Clashes(models))
		{
			var members = clash.ToList();
			var byRoot = members.Select(model => model.Chosen!.Root).Distinct(StringComparer.Ordinal).Count() > 1;
			var shared = members.SelectMany(model => origin.GetValueOrDefault(model) ?? [model]).Distinct().Select(Segments).Aggregate((left, right) => [.. left.Intersect(right, StringComparer.Ordinal)]);
			var renamed = members.Where(model => !kept.Contains(model)).ToList();

			foreach (var model in renamed)
				model.Name = Words.Identifier(byRoot
					? model.Chosen!.Root + model.Name
					: model.Chosen!.Discriminated(string.Concat(Segments(model).Where(segment => !shared.Contains(segment, StringComparer.Ordinal)))));

			diagnostics.Warn($"{clash.Key}: the verb did not separate {members.Count} shapes; renamed by {(byRoot ? "description" : "route")} to {string.Join(", ", renamed.Select(model => model.Name).Order(StringComparer.Ordinal))}.");
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

	/// <summary>The shape a GET returns; else the one reached from strictly the most descriptions; else none.</summary>
	static ModelType? Keeper(IGrouping<string, ModelType> clash)
	{
		if (clash.Where(ReachedByGet).OrderBy(model => model.ShapeKey, StringComparer.Ordinal).FirstOrDefault() is { } fromGet)
			return fromGet;

		var widest = clash
			.Select(model => (Model: model, Count: model.Candidates.Select(candidate => candidate.Source).Distinct(StringComparer.Ordinal).Count()))
			.OrderByDescending(pair => pair.Count)
			.ToList();

		return widest[0].Count > widest[1].Count ? widest[0].Model : null;
	}

	static string KeptBy(ModelType? keeper)
		=> keeper is null ? "none" : ReachedByGet(keeper) ? "the shape a GET returns" : "the shape the most descriptions share";

	static string[] Segments(ModelType model) => model.Chosen!.Route.Split('/', StringSplitOptions.RemoveEmptyEntries);

	static bool ReachedByGet(ModelType model)
		=> model.Candidates.Any(candidate => candidate.FromGet && candidate.Plain == model.Chosen!.Plain);

	static List<IGrouping<string, ModelType>> Clashes(IReadOnlyList<ModelType> models)
		=> [.. models.GroupBy(model => model.Name, StringComparer.Ordinal).Where(group => group.Count() > 1).OrderBy(group => group.Key, StringComparer.Ordinal)];
}
