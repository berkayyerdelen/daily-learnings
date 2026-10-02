using prepaid.api;
using prepaid.api.Controllers;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddSingleton<IBookingService, BookingService>();
builder.Services.AddSingleton<IRefundLedger, InMemoryRefundLedger>();
builder.Services.AddScoped<IRefundService, RefundService>();
builder.Services.AddHttpClient<IPaymentService, PaymentService>((sp, client) =>
{
    var baseUrl = sp.GetRequiredService<IConfiguration>()["PaymentApi:BaseUrl"];
    client.BaseAddress = new Uri(baseUrl!);
}).AddStandardResilienceHandler();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program
{
}