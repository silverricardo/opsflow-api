namespace OpsFlow.Api.Persistence.Configurations;

internal static class SqlConstraints
{
    /// <summary>Check-constraint SQL limiting a string-stored enum column to the enum's names.</summary>
    public static string InEnum<TEnum>(string column) where TEnum : struct, Enum =>
        $"[{column}] IN ({string.Join(", ", Enum.GetNames<TEnum>().Select(name => $"'{name}'"))})";
}
