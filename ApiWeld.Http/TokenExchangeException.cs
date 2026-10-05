namespace ApiWeld.Http;

/// <summary>The key-for-token exchange failed or returned no token.</summary>
public sealed class TokenExchangeException(string message) : Exception(message);
