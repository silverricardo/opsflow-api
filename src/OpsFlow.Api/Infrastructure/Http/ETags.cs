namespace OpsFlow.Api.Infrastructure.Http;

/// <summary>Strong ETags derived from the aggregate's SQL Server <c>rowversion</c> (ADR-004).</summary>
public static class ETags
{
    public const string IfMatchHeader = "If-Match";

    public static string From(byte[] rowVersion) => $"\"{Convert.ToBase64String(rowVersion)}\"";

    public static bool Matches(string ifMatch, byte[] rowVersion) =>
        string.Equals(ifMatch.Trim(), From(rowVersion), StringComparison.Ordinal);

    public static void SetETag(this HttpResponse response, byte[] rowVersion) =>
        response.Headers.ETag = From(rowVersion);
}
