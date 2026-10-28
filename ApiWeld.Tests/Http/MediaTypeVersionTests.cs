using ApiWeld.Http;

namespace ApiWeld.Tests.Http;

public class MediaTypeVersionTests
{
	[Theory]
	[InlineData("application/vnd.example.v12.6.0+json", "12.6")]
	[InlineData("application/vnd.example.v15+json", "15")]
	[InlineData("application/vnd.example.errors.v2+json", "2")]
	[InlineData("application/vnd.example.v11.1.0+json; charset=utf-8", "11.1")]
	[InlineData("APPLICATION/VND.EXAMPLE.V3+JSON", "3")]
	[InlineData("application/vnd.example.v1", "1")]
	public void Reads_the_version_token_from_a_vendor_media_type(string mediaType, string expected)
	{
		Assert.Equal(expected, MediaTypeVersion.Read(mediaType));
	}

	[Theory]
	[InlineData("application/json")]
	[InlineData("text/plain")]
	[InlineData("application/vnd.example.vendor+json")]
	[InlineData("")]
	[InlineData(null)]
	public void Reads_no_version_from_a_media_type_without_one(string? mediaType)
	{
		Assert.Null(MediaTypeVersion.Read(mediaType));
	}

	[Theory]
	[InlineData("15.0.0", "15")]
	[InlineData("15.0", "15")]
	[InlineData("15", "15")]
	[InlineData("12.6.0", "12.6")]
	[InlineData("1.0.0", "1")]
	[InlineData("0", "0")]
	[InlineData("v7.0", "7")]
	[InlineData("01.02", "1.2")]
	public void Normalizes_by_dropping_trailing_zero_parts(string version, string expected)
	{
		Assert.Equal(expected, MediaTypeVersion.Normalize(version));
	}

	[Theory]
	[InlineData("12.6", "V12_6")]
	[InlineData("15", "V15")]
	[InlineData(null, "V0")]
	public void Names_the_version_member(string? version, string expected)
	{
		Assert.Equal(expected, MediaTypeVersion.MemberName(version));
	}

	[Theory]
	[InlineData("11.4", "11.1", true)]
	[InlineData("11.4", "11", true)]
	[InlineData("11.1.2", "11.1", true)]
	[InlineData("11.10", "11.9", true)]
	[InlineData("11.1", "11.1", false)]
	[InlineData("11.1", "11.4", false)]
	[InlineData("12", "11.1", false)]
	[InlineData("11.4.0-beta", "11.1", false)]
	public void Tells_a_newer_version_within_the_same_major(string candidate, string baseline, bool expected)
	{
		Assert.Equal(expected, MediaTypeVersion.IsNewerInMajor(candidate, baseline));
	}

	[Theory]
	[InlineData("application/vnd.example.v11.1.0+json", "11.4", "application/vnd.example.v11.4.0+json")]
	[InlineData("application/vnd.example.v11+json", "11.4", "application/vnd.example.v11.4+json")]
	[InlineData("application/vnd.example.v11.1.0+json; charset=utf-8", "11.4", "application/vnd.example.v11.4.0+json; charset=utf-8")]
	[InlineData("application/json", "11.4", "application/json")]
	public void Rewrites_the_version_token_keeping_its_number_of_parts(string mediaType, string version, string expected)
	{
		Assert.Equal(expected, MediaTypeVersion.WithVersion(mediaType, version));
	}
}
