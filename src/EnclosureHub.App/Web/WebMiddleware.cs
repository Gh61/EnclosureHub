using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace EnclosureHub.App.Web;

internal static class WebMiddleware
{
    private static readonly PathString ApiPrefix = "/api";
    private static readonly PathString HealthPath = "/health";

    /// <summary>
    /// HTTP Basic authentication when <c>Web:Auth:Username</c> is set. Covers everything except /health
    /// so monitoring keeps working. Options are read per request, so changes apply after a config reload.
    /// </summary>
    public static IApplicationBuilder UseBasicAuth(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            var auth = context.RequestServices.GetRequiredService<IOptionsMonitor<WebOptions>>().CurrentValue.Auth;

            if (string.IsNullOrEmpty(auth.Username) || context.Request.Path.Equals(HealthPath) || IsAuthorized(context.Request, auth))
            {
                await next(context);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Basic realm=\"Enclosure Hub\", charset=\"UTF-8\"";
            await context.Response.WriteAsync("Authentication required.");
        });
    }

    /// <summary>Returns 404 for the dashboard (everything outside /api and /health) while <c>Web:Dashboard:Enabled</c> is false.</summary>
    public static IApplicationBuilder UseDashboardSwitch(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            var path = context.Request.Path;
            if (path.StartsWithSegments(ApiPrefix) || path.Equals(HealthPath))
            {
                await next(context);
                return;
            }

            var dashboard = context.RequestServices.GetRequiredService<IOptionsMonitor<WebOptions>>().CurrentValue.Dashboard;
            if (!dashboard.Enabled)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsync("The web dashboard is disabled (Web:Dashboard:Enabled).");
                return;
            }

            await next(context);
        });
    }

    private static bool IsAuthorized(HttpRequest request, WebAuthOptions auth)
    {
        string? header = request.Headers.Authorization;
        if (header == null || !header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            return false;

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header[6..].Trim()));
        }
        catch (FormatException)
        {
            return false;
        }

        int colon = decoded.IndexOf(':');
        if (colon < 0)
            return false;

        return FixedTimeEquals(decoded[..colon], auth.Username) & FixedTimeEquals(decoded[(colon + 1)..], auth.Password);
    }

    private static bool FixedTimeEquals(string a, string b)
        => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
