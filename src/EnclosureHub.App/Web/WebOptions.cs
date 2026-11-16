namespace EnclosureHub.App.Web;

/// <summary>Bound from "Web".</summary>
public sealed class WebOptions
{
    public WebDashboardOptions Dashboard { get; set; } = new();

    public WebAuthOptions Auth { get; set; } = new();
}

public sealed class WebDashboardOptions
{
    /// <summary>Serves the web dashboard at "/". The API keeps working when false.</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// HTTP Basic authentication for everything the server serves (dashboard, API, camera endpoints)
/// except <c>/health</c>. Active when <see cref="Username"/> is not empty.
/// </summary>
public sealed class WebAuthOptions
{
    public string Username { get; set; } = "";

    public string Password { get; set; } = "";
}
