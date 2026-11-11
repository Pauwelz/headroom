namespace Headroom.Endpoints;

public static class ApiEndpoints
{
    // Each feature adds one endpoint group here.
    public static void MapApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");
        api.MapInstanceEndpoints();
        api.MapMeEndpoints();
        api.MapPeopleEndpoints();
        api.MapSettingsEndpoints();
        api.MapAccessTokenEndpoints();
        api.MapPasskeyEndpoints();
        api.MapWebhookEndpoints();
        api.MapEmailServerEndpoints();
        api.MapConnectedAppEndpoints();
        api.MapConnectionEndpoints();
        api.MapProjectEndpoints();
        api.MapScheduleEndpoints();
        api.MapTimeOffEndpoints();
        api.MapAllocationEndpoints();
        api.MapUtilisationEndpoints();
        api.MapHelpEndpoints();
    }
}
