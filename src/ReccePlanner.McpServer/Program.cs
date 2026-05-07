using ReccePlanner.McpServer;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithTools<RecceTools>();

var app = builder.Build();

app.MapMcp();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
