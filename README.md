# Sentry .NET

Proof of Concept (POC) per mostrare come integrare Sentry in una Web API .NET e fornire una piccola demo + materiale di presentazione.

## Mappa del repository

```
Sentry.Monitored.App.sln
README.md (questo file)
Sentry.Monitored.App.WebApi/
  Program.cs
  appsettings.json
  appsettings.Development.json
  Properties/launchSettings.json
  Sentry.Monitored.App.WebApi.csproj
  WebApi.http (richieste di esempio)
  bin/ ... (output compilazione)
  obj/ ... (artefatti di build)
demo/ (piccola demo di utilizzo / esempi aggiuntivi)
presentazione/ (slide Slidev sulla POC e su Sentry)  <-- se non presente, da creare
```
Se alcune cartelle (es. `presentazione/`) non sono ancora presenti, crearle manualmente.

## Cos'è Sentry

[Sentry](https://sentry.io/) è una piattaforma di Application Monitoring che permette di:
- Tracciare errori (Issue Tracking) con stack trace dettagliati
- Rilevare performance (transaction tracing, Apdex, breakdown per span)
- Collegare release e distribuzioni per capire quando è stato introdotto un problema
- Aggiungere contesto (utente, tag, extra, breadcrumbs) per facilitare la diagnosi

Documentazione .NET: https://docs.sentry.io/platforms/dotnet/

## Obiettivi della POC
1. Configurare un nuovo progetto Sentry lato piattaforma
2. Integrare il SDK .NET in una Web API
3. Inviare errori e transazioni di performance
4. Mostrare una demo di generazione errore e tracing
5. Materiale slide per spiegare il flusso

## Prerequisiti
- .NET SDK (versione >= 8/9/10 a seconda del progetto — il csproj indica `net10.0`)
- Account Sentry (gratuito o trial)
- Accesso alla rete per inviare gli eventi (porta 443 HTTPS)

## Passaggi sulla piattaforma Sentry
1. Registrati / Accedi a https://sentry.io/
2. Crea un'Organizzazione (se non esiste) o usa quella esistente
3. Vai su Projects > Create Project
4. Seleziona piattaforma: .NET (ASP.NET Core)
5. Dai un nome al progetto (es: `sentry-monitored-web-api`)
6. Annota la DSN generata (formato: `https://<key>@o<org>.ingest.sentry.io/<projectId>`) — NON committare chiavi sensibili in chiaro se il repo è pubblico
7. (Opzionale) Configura:
   - Environments (es: `Development`, `Staging`, `Production`)
   - Alert Rules (error rate, regression, performance)
   - Performance (abilitato di default nei piani compatibili)
   - Sample Rate per tracing (consigliato iniziare con 1.0 in dev e ridurre in prod)
8. (Opzionale) Imposta un Release naming convention (es: `sentry-monitored-web-api@1.0.0+build123`)
9. Verifica nel Project Settings > Client Keys che la DSN sia attiva

## Passaggi nell'applicazione Web API .NET
### 1. Aggiunta pacchetto
Nel file csproj o via CLI:
```
dotnet add Sentry.Monitored.App.WebApi package Sentry.AspNetCore
```
(Se serve tracing avanzato aggiunge automaticamente le dipendenze necessarie.)

### 2. Configurazione di base in `Program.cs`
Inserisci prima del `builder.Build()`:
```csharp
builder.WebHost.UseSentry(options =>
{
    options.Dsn = builder.Configuration["Sentry:Dsn"]; // Legge da appsettings o segreti utente
    options.Debug = true;            // Solo in Development
    options.TracesSampleRate = 1.0;  // 100% in Dev; riduci es. 0.2 in Production
    options.Environment = builder.Environment.EnvironmentName; // Development/Production
    // options.Release = "sentry-monitored-web-api@1.0.0"; // Imposta se gestisci versioning
});

builder.Services.AddSentry(); // Registra integrazione per DI, logging, ecc.
```

### 3. Configurazione in `appsettings.json`
```json
{
  "Sentry": {
    "Dsn": "https://<publicKey>@o<org>.ingest.sentry.io/<projectId>",
    "SendDefaultPii": true,
    "AttachStacktrace": true
  }
}
```
In ambienti protetti usa Secret Manager (`dotnet user-secrets`) o variabili d'ambiente per la DSN.

### 4. Cattura errori manuale

La cattura degli errori avviene automaticamente, tutte le eccezioni sono inviate a Sentry, ma puoi anche farlo manualmente.
In una Web API le eccezioni non gestite sono già intercettate dal middleware Sentry.

```csharp
using Sentry;

try
{
    // codice che genera eccezione
    throw new InvalidOperationException("Errore di test POC");
}
catch (Exception ex)
{
    SentrySdk.CaptureException(ex); // Invia a Sentry
}
```

### 5. Aggiunta di contesto
Con Sentry puoi aggiungere informazioni utili per la diagnosi creando un contesto nello scope corrente:
```csharp
SentrySdk.ConfigureScope(scope =>
{
    scope.User = new User { Id = "42", Email = "user@example.com" };
    scope.SetTag("feature", "beta-endpoint");
    scope.SetExtra("payload_size", 1234);
});
```
Queste dati vengono inviate con ogni evento, di logging successivo alla configurazione.
La collocazione ideale per questi settaggi è in un middleware custom o in un filtro action.

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
Transazioni e span vengono creati automaticamente (ASP.NET Core pipeline, HTTP client). Per span custom:
```csharp
var transaction = SentrySdk.StartTransaction("demo-request", "webapi.request");
var span = transaction.StartChild("custom.work", "Elaborazione business logic");
// ... codice
span.Finish();
transaction.Finish();
```
In un controller spesso non è necessario: il middleware genera transazioni per ogni request, e grazie a questo fornisce una tempistica di risposta per ogni richiesta (automaticamente).
Queste impostazioni servono ad effettuare una sampling aggiuntiva.

### 7. Logging integration
Se usi `ILogger`, i log con livello Error+ vengono inviati come breadcrumbs / eventi.
Configurabile tramite `AddSentry()`.

### 8. Release e Deploy
Per collegare issues ad una release:
```csharp
options.Release = "sentry-monitored-web-api@1.0.0";
```
Puoi inviare anche info deploy via CLI (facoltativo): questo aiuta a tracciare quando un issue è stato introdotto, quale commit.


### 9. Simulare errori e verificare
Avvia l'app (`dotnet run --project Sentry.Monitored.App.WebApi`) e chiama un endpoint che genera un'eccezione. Entro pochi secondi dovresti vedere l'issue nel pannello Sentry.

## Demo
La cartella `demo/` può contenere:
- Script/endpoint per generare errori e transazioni
- File `.http` (`WebApi.http`) già presente per testare richieste
- Eventuali snippet per confronto prima/dopo

## Presentazione (Slidev)
Creare directory `presentazione/` con contenuto Slidev:
```
presentazione/
  slides.md
  package.json
```
Esempio comandi:
```powershell
cd presentazione
npm init -y
npm install @slidev/cli @slidev/theme-default
npx slidev slides.md
```

## Link utili
- Homepage: https://sentry.io/
- Docs .NET: https://docs.sentry.io/platforms/dotnet/
- Performance: https://docs.sentry.io/product/performance/
- Release Management: https://docs.sentry.io/product/releases/

---
POC mantenuta a scopo dimostrativo. Adatta i parametri (DSN, SampleRate, Release) prima di produzione.
