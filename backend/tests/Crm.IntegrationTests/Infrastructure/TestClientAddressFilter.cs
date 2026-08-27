using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Crm.IntegrationTests.Infrastructure;

/// <summary>
/// Lets a test present itself as arriving from a particular address.
///
/// The in-memory test server has no socket, so every request looks like it came from nowhere -
/// which would put them all in one throttling partition and make "one abusive caller must not
/// consume everybody else's allowance" untestable. This runs before the application's own pipeline
/// and sets the address from a header, so a test can be two callers.
///
/// Only ever registered by the test host: nothing in the application reads this header.
/// </summary>
internal sealed class TestClientAddressFilter : IStartupFilter
{
    public const string HeaderName = "X-Test-Source";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return app =>
        {
            app.Use(async (context, continuation) =>
            {
                if (context.Request.Headers.TryGetValue(HeaderName, out var source)
                    && IPAddress.TryParse(source.ToString(), out var address))
                {
                    context.Connection.RemoteIpAddress = address;
                }

                await continuation();
            });

            next(app);
        };
    }
}
