# Sentry .NET

Proof of Concept (POC) to show how to integrate Sentry into a .NET Web API and provide a small demo + presentation material.

## Repository Map

```text
sentry-monitored-web-app/
├── README.md
├── demo/
│   ├── Sentry.Monitored.App.sln
│   ├── Sentry.Monitored.App.WebApi/
│   │   ├── Program.cs
│   │   ├── appsettings.json
│   │   ├── appsettings.Development.json (if present)
│   │   ├── Properties/
│   │   │   └── launchSettings.json
│   │   ├── Sentry.Monitored.App.WebApi.csproj
│   │   ├── WebApi.http (for manual testing)
│   │   ├── bin/ (build output)
│   │   └── obj/ (build artifacts)
└── presentation/ (Slidev presentation)
    ├── slides.md
    └── package.json
```
If optional folders (e.g. `presentazione/`) are missing you can create them as needed.

## What is Sentry

[Sentry](https://sentry.io/) is an Application Monitoring platform that lets you:
- Track errors (Issue Tracking) with detailed stack traces
- Detect performance issues (transaction tracing, Apdex, span breakdown)
- Link releases and deployments to understand when a problem was introduced
- Add context (user, tags, extras, breadcrumbs) to ease diagnosis

.NET docs: https://docs.sentry.io/platforms/dotnet/

## POC Objectives
1. Configure a new Sentry project on the platform
2. Integrate the .NET SDK into a Web API
3. Send errors and performance transactions
4. Show a demo generating an error and tracing
5. Provide slides explaining the overall flow

## Prerequisites
- .NET SDK (version >= 8/9/10 — the csproj targets `net10.0`)
- Sentry account (free or trial)
- Network access (HTTPS port 443) to send events

## Steps on the Sentry Platform
1. Register / Log in at https://sentry.io/
2. Create or use an existing Organization
3. Go to Projects > Create Project
4. Select platform: .NET (ASP.NET Core)
5. Name the project (e.g. `sentry-monitored-web-api`)
6. Note the DSN generated (format: `https://<key>@o<org>.ingest.sentry.io/<projectId>`) — DO NOT commit sensitive keys if the repo is public
7. (Optional) Configure:
   - Environments (`Development`, `Staging`, `Production`)
   - Alert Rules (error rate, regression, performance)
   - Performance (enabled by default in eligible plans)
   - Sample Rate for tracing (start with 1.0 in dev; reduce in production)
8. (Optional) Set a Release naming convention (e.g. `sentry-monitored-web-api@1.0.0+build123`)
9. Verify in Project Settings > Client Keys that the DSN is active

## Steps in the .NET Web API Application
### 1. Add package
In the csproj or via CLI (note path inside `demo/`):
```
dotnet add demo/Sentry.Monitored.App.WebApi package Sentry.AspNetCore
```
(Advanced tracing dependencies are added automatically.)

### 2. Basic configuration in `Program.cs`
Insert before `builder.Build()`:
```csharp
builder.WebHost.UseSentry(options =>
{
    options.Dsn = builder.Configuration["Sentry:Dsn"]; // Reads from appsettings or user secrets
    options.Debug = true;            // Development only
    options.TracesSampleRate = 1.0;  // 100% in Dev; reduce e.g. 0.2 in Production
    options.Environment = builder.Environment.EnvironmentName; // Development/Production
    // options.Release = "sentry-monitored-web-api@1.0.0"; // Set if you manage versioning
});

builder.Services.AddSentry(); // Registers integration for DI, logging, etc.
```

### 3. Configuration in `appsettings.json`
```json
{
  "Sentry": {
    "Dsn": "https://<publicKey>@o<org>.ingest.sentry.io/<projectId>",
    "SendDefaultPii": true,
    "AttachStacktrace": true
  }
}
```
In protected environments use Secret Manager (`dotnet user-secrets`) or environment variables for the DSN.

### 4. Manual error capture
Error capture happens automatically; all unhandled exceptions are sent to Sentry. You can also do it manually.
In a Web API unhandled exceptions are already captured by the Sentry middleware.

```csharp
using Sentry;

try
{
    // Code that throws
    throw new InvalidOperationException("Test POC error");
}
catch (Exception ex)
{
    SentrySdk.CaptureException(ex); // Sends to Sentry
}
```

### 5. Adding context
With Sentry you can add helpful diagnostic info by creating context in the current scope:
```csharp
SentrySdk.ConfigureScope(scope =>
{
    scope.User = new User { Id = "42", Email = "user@example.com" };
    scope.SetTag("feature", "beta-endpoint");
    scope.SetExtra("payload_size", 1234);
});
```
These values are sent with every event/log captured after the configuration. The ideal place for these settings is in custom middleware or an action filter.

```csharp
public sealed class SentryRequestContextMiddleware
{
    private readonly RequestDelegate _next;
    private static readonly string CorrelationHeader = "X-Correlation-Id";

    public SentryRequestContextMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        using (SentrySdk.PushScope())
        {
            var correlationId = GetOrCreateCorrelationId(context);
            var route = context.Request.Path.HasValue ? context.Request.Path.Value : "/";
            SentrySdk.ConfigureScope(scope =>
            {
                // Tags (searchable, low cardinality)
                scope.SetTag("http.method", context.Request.Method);
                scope.SetTag("http.route", route);
                scope.SetTag("service", "webapi");

                // Extras (high cardinality / detailed)
                scope.SetExtra("correlation_id", correlationId);
                scope.SetExtra("query_string", context.Request.QueryString.HasValue ? context.Request.QueryString.Value : string.Empty);
                scope.SetExtra("remote_ip", context.Connection.RemoteIpAddress?.ToString());

                // User (avoid PII unless needed)
                if (context.User?.Identity?.IsAuthenticated == true)
                {
                    var userId = context.User.FindFirst("sub")?.Value
                                 ?? context.User.FindFirst("id")?.Value;
                    scope.User = new User
                    {
                        Id = userId,
                        Username = context.User.Identity.Name,
                        Email = context.User.FindFirst("email")?.Value
                    };
                }
            });

            SentrySdk.AddBreadcrumb(
                message: "Incoming request",
                category: "http",
                type: "navigation",
                data: new Dictionary<string, string>
                {
                    { "path", route },
                    { "method", context.Request.Method },
                    { "correlation_id", correlationId }
                });

            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                // Explicit capture (Sentry ASP.NET Core also auto-captures)
                SentrySdk.CaptureException(ex);
                throw;
            }
        }
    }

    private static string GetOrCreateCorrelationId(HttpContext ctx)
    {
        if (ctx.Request.Headers.TryGetValue(CorrelationHeader, out var existing) && !StringValues.IsNullOrEmpty(existing))
            return existing!;
        var generated = ctx.TraceIdentifier ?? Guid.NewGuid().ToString("n");
        ctx.Response.Headers[CorrelationHeader] = generated;
        return generated;
    }
}

```

### 6. Performance (tracing)
Transactions and spans are created automatically (ASP.NET Core pipeline, HTTP client). For custom spans:
```csharp
var transaction = SentrySdk.StartTransaction("demo-request", "webapi.request");
var span = transaction.StartChild("custom.work", "Business logic processing");
// ... code
span.Finish();
transaction.Finish();
```
In a controller this is often not necessary: the middleware creates a transaction per request, giving response timing automatically. These settings let you do additional sampling.

### 7. Logging integration
If you use `ILogger`, logs with level Error+ are sent as breadcrumbs / events. Configurable via `AddSentry()`.

### 8. Release & Deploy
To associate issues to a release:
```csharp
options.Release = "sentry-monitored-web-api@1.0.0";
```
You can also send deploy info via CLI (optional): helps track when an issue was introduced and which commit.

### 9. Simulate errors and verify
Run the app:
```
dotnet run --project demo/Sentry.Monitored.App.WebApi
```
Call an endpoint that throws an exception. Within a few seconds you should see the issue in Sentry.

## Demo
The `demo/` folder contains the solution and project. You can add:
- Scripts/endpoints to generate errors and transactions
- The `.http` file (`WebApi.http`) already present for manual request testing
- Snippets to compare before/after

## Presentation (Slidev)
Create directory `presentazione/` with Slidev content:
```
presentazione/
  slides.md
  package.json
```
Example commands:
```powershell
cd presentazione
npm init -y
npm install @slidev/cli @slidev/theme-default
npx slidev slides.md
```

## Useful Links
- Homepage: https://sentry.io/
- .NET Docs: https://docs.sentry.io/platforms/dotnet/
- Performance: https://docs.sentry.io/product/performance/
- Release Management: https://docs.sentry.io/product/releases/

---
POC maintained for demonstration purposes. Adjust parameters (DSN, SampleRate, Release) before going to production.
