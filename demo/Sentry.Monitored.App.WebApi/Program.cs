using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();
builder.Services.AddSentry();

builder.WebHost.UseSentry(options =>
{
    options.Dsn = builder.Configuration["Sentry:Dsn"];
    options.Debug = true;
    options.TracesSampleRate = 1.0;
    options.Experimental.EnableLogs = true;
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();



var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
    {
        var forecast = Enumerable.Range(1, 5).Select(index =>
                new WeatherForecast
                (
                    DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                    Random.Shared.Next(-20, 55),
                    summaries[Random.Shared.Next(summaries.Length)]
                ))
            .ToArray();
        return forecast;
    })
    .WithName("GetWeatherForecast");

app.MapGet("/checkSentry", () =>
    {
        SentrySdk.CaptureMessage("This is a test message");
    })
    .WithName("CheckSentryCaptureMessage");

app.MapGet("/checkSentryWithException", () =>
    {
        throw new Exception("This is a test exception");
    })
    .WithName("CheckSentryException");

app.MapGet("/checkSentryWithLogging", ([FromServices]ILogger<Program> logger) =>
    {
        logger.LogError("This is a test log");
        logger.LogInformation("This is a test log");
        logger.LogWarning("This is a test log");
        logger.LogDebug("This is a test log");
    })
    .WithName("CheckSentryLog");


app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}