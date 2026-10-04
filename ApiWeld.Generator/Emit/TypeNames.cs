using ApiWeld.Generator.Model;

namespace ApiWeld.Generator.Emit;

/// <summary>Spells a type reference in C#.</summary>
internal static class TypeNames
{
	public static string Of(TypeRef type) => type switch
	{
		ScalarRef scalar => scalar.Name,
		ListRef list => $"List<{Of(list.Item)}>",
		MapRef map => $"Dictionary<string, {Of(map.Value)}>",
		ModelRef model => model.Model.Name,
		_ => throw new InvalidOperationException($"Unknown type {type}.")
	};

	public static string Nullable(TypeRef type) => Of(type) + "?";

	/// <summary>Whether the type is a class, which an error factory's type argument must be.</summary>
	public static bool IsClass(TypeRef type) => type switch
	{
		ScalarRef scalar => !scalar.IsValueType,
		ModelRef model => model.Model.Kind == ModelKind.Object,
		_ => true
	};
}
