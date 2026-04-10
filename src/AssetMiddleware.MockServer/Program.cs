using WireMock.Logging;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using WireMock.Settings;

const int port = 9090;

var server = WireMockServer.Start(new WireMockServerSettings
{
    Port = port,
    Logger = new WireMockConsoleLogger()
});

Console.WriteLine($"WireMock server running at http://localhost:{port}");
Console.WriteLine("Registered stubs:");

// ── 1. Token endpoint ──────────────────────────────────────────────────────
server.Given(Request.Create()
        .WithPath("/oauth/token")
        .UsingPost())
    .RespondWith(Response.Create()
        .WithStatusCode(200)
        .WithHeader("Content-Type", "application/json")
        .WithBodyAsJson(new
        {
            access_token = "mock-token-" + Guid.NewGuid().ToString("N")[..8],
            token_type = "Bearer",
            expires_in = 5400,
            created_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        }));

Console.WriteLine("  POST /oauth/token → 200");

// ── 2. Asset statuses ──────────────────────────────────────────────────────
server.Given(Request.Create()
        .WithPath("/v1/companies/*/asset-statuses")
        .UsingGet())
    .RespondWith(Response.Create()
        .WithStatusCode(200)
        .WithHeader("Content-Type", "application/json")
        .WithBodyAsJson(new
        {
            data = new[]
            {
                new { id = 1, name = "Active" },
                new { id = 2, name = "Inactive" },
                new { id = 3, name = "Retired" }
            }
        }));

Console.WriteLine("  GET  /v1/companies/*/asset-statuses → 200");

// ── 3. Asset search — default: no match (safe to create) ──────────────────
server.Given(Request.Create()
        .WithPath("/v1/projects/*/assets")
        .UsingGet())
    .AtPriority(10)
    .RespondWith(Response.Create()
        .WithStatusCode(200)
        .WithHeader("Content-Type", "application/json")
        .WithBodyAsJson(new { data = Array.Empty<object>() }));

Console.WriteLine("  GET  /v1/projects/*/assets?search=* → 200 empty");

// ── 3b. Asset search — match for a known asset (check-in flow) ────────────
server.Given(Request.Create()
        .WithPath("/v1/projects/*/assets")
        .UsingGet()
        .WithParam("search", "Caterpillar-320-SN9901"))
    .AtPriority(1)
    .RespondWith(Response.Create()
        .WithStatusCode(200)
        .WithHeader("Content-Type", "application/json")
        .WithBodyAsJson(new
        {
            data = new[]
            {
                new { id = "asset-001", assetId = "Caterpillar-320-SN9901", onsite = false }
            }
        }));

Console.WriteLine("  GET  /v1/projects/*/assets?search=Caterpillar-320-SN9901 → 200 match");

// ── 4. Asset create ────────────────────────────────────────────────────────
server.Given(Request.Create()
        .WithPath("/v1/projects/*/assets")
        .UsingPost())
    .RespondWith(Response.Create()
        .WithStatusCode(201)
        .WithHeader("Content-Type", "application/json")
        .WithBodyAsJson(new
        {
            data = new
            {
                id = "asset-" + Guid.NewGuid().ToString("N")[..6],
                assetId = "Caterpillar-320-SN-9901"
            }
        }));

Console.WriteLine("  POST /v1/projects/*/assets → 201");

// ── 5. Asset update (PATCH) ────────────────────────────────────────────────
server.Given(Request.Create()
        .WithPath("/v1/projects/*/assets/*")
        .UsingPatch())
    .RespondWith(Response.Create()
        .WithStatusCode(200)
        .WithHeader("Content-Type", "application/json")
        .WithBodyAsJson(new { data = new { id = "asset-001", onsite = true } }));

Console.WriteLine("  PATCH /v1/projects/*/assets/* → 200");

// ── 6. Photo upload ────────────────────────────────────────────────────────
server.Given(Request.Create()
        .WithPath("/v1/projects/*/assets/*/attachments")
        .UsingPost())
    .RespondWith(Response.Create()
        .WithStatusCode(201));

Console.WriteLine("  POST /v1/projects/*/assets/*/attachments → 201");

Console.WriteLine();
Console.WriteLine("Press [Enter] to stop.");
Console.ReadLine();

server.Stop();

