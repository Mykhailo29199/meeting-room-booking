using MeetingRoomBooking.Api.Errors;
using MeetingRoomBooking.Application.Bookings;
using MeetingRoomBooking.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Database, unit of work and repositories (needs ConnectionStrings:Default).
builder.Services.AddInfrastructure(builder.Configuration);

// Use cases. TimeProvider is injected (not DateTime.UtcNow) so tests can fix "now".
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<BookingService>();

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Errors: application exceptions -> 400/403/404/409 problem details;
// anything else -> generic 500 problem details.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApplicationExceptionHandler>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
