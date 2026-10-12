using OpsFlow.Domain.Common;

namespace OpsFlow.Domain.Requests;

public static class RequestErrors
{
    public static readonly DomainError AdminCannotCreate = new(
        "request.admin_cannot_create", "Administrators cannot create requests.", ErrorType.Forbidden);

    public static readonly DomainError NotOwner = new(
        "request.not_owner", "Only the requester can perform this action.", ErrorType.Forbidden);

    public static readonly DomainError RequesterHasNoManager = new(
        "request.requester_has_no_manager",
        "The requester has no manager, so the approval chain cannot be created.",
        ErrorType.Unprocessable);

    public static readonly DomainError FulfilNotAllowed = new(
        "request.fulfil_not_allowed", "Only Finance can mark a request as fulfilled.", ErrorType.Forbidden);

    public static readonly DomainError SelfFulfilment = new(
        "request.self_fulfilment", "You cannot mark your own request as fulfilled.", ErrorType.Forbidden);

    public static DomainError InvalidTransition(RequestStatus from, string action) => new(
        "request.invalid_transition",
        $"Cannot {action} a request in status '{from}'.",
        ErrorType.Conflict);

    public static DomainError InvalidTitle => new(
        "request.invalid_title",
        $"Title must be between {Request.TitleMinLength} and {Request.TitleMaxLength} characters.",
        ErrorType.Validation);

    public static DomainError InvalidDescription => new(
        "request.invalid_description",
        $"Description must be at most {Request.DescriptionMaxLength} characters.",
        ErrorType.Validation);

    public static DomainError InvalidAmount => new(
        "request.invalid_amount",
        $"Amount must be greater than 0, at most {Request.MaxAmount:N2}, with at most two decimal places.",
        ErrorType.Validation);
}
