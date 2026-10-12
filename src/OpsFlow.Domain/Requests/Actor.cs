using OpsFlow.Domain.Users;

namespace OpsFlow.Domain.Requests;

/// <summary>The authenticated user performing an operation.</summary>
public readonly record struct Actor(int UserId, UserRole Role);
