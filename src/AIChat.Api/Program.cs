using System.Text.Json.Serialization;
using AIChat.Application;
using AIChat.Api.Endpoints;
using AIChat.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddCors(options =>
{
    options.AddPolicy("MvpWebConsole", policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors("MvpWebConsole");

app.MapGet("/", () => Results.Ok(new
{
    service = "AIChat.Api",
    status = "Running",
    utcTime = DateTimeOffset.UtcNow
}));

app.MapHealthChecks("/health");
app.MapM2Endpoints();
app.MapM3Endpoints();

app.Run();
