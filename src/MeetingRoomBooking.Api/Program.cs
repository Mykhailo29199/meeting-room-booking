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
