using Microsoft.Extensions.Options;
using prepaid.api.Clients.CustomerDirectory;
using prepaid.api.Clients.Payments;
using prepaid.api.Features.Bookings;
using prepaid.api.Features.Refunds;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddSingleton<IBookingService, BookingService>();
builder.Services.AddSingleton<IRefundLedger, InMemoryRefundLedger>();
builder.Services.AddScoped<IRefundService, RefundService>();

builder.Services.AddOptions<PaymentApiOptions>()
    .Bind(builder.Configuration.GetSection("PaymentApi"))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddHttpClient<IPaymentApiClient, PaymentApiClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<PaymentApiOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
}).AddStandardResilienceHandler();

builder.Services.AddOptions<CustomerDirectoryOptions>()
    .Bind(builder.Configuration.GetSection("CustomerDirectory"))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddHttpClient<ICustomerDirectoryClient, CustomerDirectoryClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<CustomerDirectoryOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
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