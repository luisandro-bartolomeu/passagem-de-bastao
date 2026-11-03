var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Root & Health endpoints
app.MapGet("/", () => Results.Ok(new { message = "IGF Academic Repository API" }))
   .WithName("GetRoot");

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }))
   .WithName("GetHealth");

app.Run();

// Expose Program class for WebApplicationFactory in integration tests
public partial class Program { }
