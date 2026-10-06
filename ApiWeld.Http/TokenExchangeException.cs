namespace ApiWeld.Http;

/// <summary>The key-for-token exchange failed, could not be completed, or returned no token.</summary>
public sealed class TokenExchangeException : Exception
{
	/// <summary>An exchange failure described by <paramref name="message"/>.</summary>
	public TokenExchangeException(string message) : base(message) { }

	/// <summary>An exchange that could not be completed because of <paramref name="inner"/>.</summary>
	public TokenExchangeException(string message, Exception inner) : base(message, inner) { }
}
