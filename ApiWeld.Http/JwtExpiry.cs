using System.Text.Json;

namespace ApiWeld.Http;

/// <summary>Reads a JWT's <c>exp</c> claim without validating it; the token came from our own exchange.</summary>
internal static class JwtExpiry
{
	public static DateTimeOffset? Read(string token)
	{
		var parts = token.Split('.');

		if (parts.Length != 3)
			return null;

		try
		{
			var segment = parts[1].Replace('-', '+').Replace('_', '/');
			segment = segment.PadRight(segment.Length + (4 - segment.Length % 4) % 4, '=');

			using var payload = JsonDocument.Parse(Convert.FromBase64String(segment));

			return payload.RootElement.ValueKind == JsonValueKind.Object
				&& payload.RootElement.TryGetProperty("exp", out var exp)
				&& exp.ValueKind == JsonValueKind.Number
				&& exp.TryGetInt64(out var seconds)
					? DateTimeOffset.FromUnixTimeSeconds(seconds)
					: null;
		}
		catch (Exception exception) when (exception is FormatException or JsonException or ArgumentOutOfRangeException)
		{
			return null;
		}
	}
}
