namespace OpsFlow.Api.Features.Requests;

public static class RequestEndpoints
{
    public static void MapRequestEndpoints(this IEndpointRouteBuilder routes)
    {
        var requests = routes.MapGroup("/requests").WithTags("Requests");

        requests.MapCreateRequest();
        requests.MapGetRequest();
        requests.MapUpdateRequest();
        requests.MapSubmitRequest();
        requests.MapDecideStep();
        requests.MapGetRequestAudit();
    }
}
