using ApiLogging.Api.Logging;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddApiLogging(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.MapControllers();

app.Run();

public partial class Program;
