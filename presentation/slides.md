---
theme: seriph
title: Sentry – Error Monitoring per .NET
layout: cover
background: linear-gradient(135deg, #1e293b 0%, #0f172a 100%)
class: text-white
---

# Sentry
### Error Monitoring per applicazioni .NET

<div class="text-sm opacity-80 mt-4">
Monitoraggio errori • Performance • Diagnostica
</div>

---
transition: slide-left
---

# Cosa è Sentry

- Piattaforma **gratuita** per error & performance monitoring
- Raccoglie **eccezioni**, stack trace e breadcrumbs
- Analizza l’impatto sugli utenti reali
- Supporta backend, frontend, mobile e serverless
- Disponibile anche **on-premise**  
  <span class="opacity-60">Self-hosted per ambienti aziendali</span>

> Tutto ciò che riguarda error monitoring e tracing di base è **gratis**.

---
transition: slide-left
---

# Feature principali

- **Errori & crash reporting** (automatico)
- **Breadcrumbs** (log contestuali)
- **Performance Monitoring**
- **Release tracking**
- Integrazione nativa con:
  - ASP.NET Core  
  - Worker / Console  
  - Blazor Server / WASM
- Compatibile con ILogger, NLog, Serilog

---
transition: slide-left
layout: image
image: ./images/SentryErrorReportingDetails.png
class: object-cover
---

---
transition: slide-left
---

# DEMO

---
transition: slide-left
---

# Sentry vs Prometheus

Prometheus → metriche **infrastrutturali**  
Sentry → errori e performance **applicative**

|  | SENTRY | PROMETHEUS |
|--------|--------|------------|
| **Tipo monitoraggio** | Applicativo | Infrastrutturale |
| **Focus** | Errori, eccezioni, crash | CPU, RAM, HealthCheck |
| **Log applicativi** | ✔️ | ❌ |
| **Metriche custom** | ✔️ via SDK | ✔️ native |
| **Dashboard** | ✔️ | ✔️ via Grafana |
| **Alerting** | error-based | metric-based |

> Le due soluzioni insieme → osservabilità completa.

---
transition: slide-left
---

# Integrazione con .NET

### Installazione

```bash
dotnet add package Sentry.AspNetCore
```

### Configurazione ASP.NET Core

```csharp
builder.WebHost.UseSentry(o =>
{
    o.Dsn = "<dsn>";
    o.TracesSampleRate = 1.0;
});

var app = builder.Build();
app.UseSentryTracing();
```

Tutti i log e le eccezioni creati dall'applicazioni sono mandati automaticamente a Sentry.

---
transition: slide-left
class: text-indigo-700
---

### Cattura manuale

```csharp
try { ... }
catch (Exception ex)
{
    SentrySdk.CaptureException(ex);
}
```


> Errori, performance e breadcrumbs disponibili automaticamente.
