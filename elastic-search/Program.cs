using Middleware.Logging.Extensions;


var builder = WebApplication.CreateBuilder(args);

// Serilog configuration
builder.Services.AddSerilog(builder.Configuration, "Logger");
// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseErrorLogging();
app.UseRequestLogging();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();


