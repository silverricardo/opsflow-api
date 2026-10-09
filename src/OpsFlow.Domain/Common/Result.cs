using System.Diagnostics.CodeAnalysis;

namespace OpsFlow.Domain.Common;

/// <summary>Outcome of a domain operation: success, or a <see cref="DomainError"/>.</summary>
public class Result
{
    private static readonly Result SuccessInstance = new(null);

    protected Result(DomainError? error) => Error = error;

    public DomainError? Error { get; }

    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    public static Result Success() => SuccessInstance;

    public static Result Failure(DomainError error) => new(error);

    public static implicit operator Result(DomainError error) => Failure(error);
}

/// <summary>Outcome of a domain operation that produces a value on success.</summary>
public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(T value) : base(null) => _value = value;

    private Result(DomainError error) : base(error) { }

    /// <summary>The value. Only valid when <see cref="Result.IsSuccess"/> is true.</summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Cannot read the value of a failed result ({Error.Code}).");

    public static implicit operator Result<T>(T value) => new(value);

    public static implicit operator Result<T>(DomainError error) => new(error);
}
