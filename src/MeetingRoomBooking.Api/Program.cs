using MeetingRoomBooking.Api.Errors;
using MeetingRoomBooking.Application.Bookings;
using MeetingRoomBooking.Infrastructure;
using MeetingRoomBooking.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// "Now" is injected (not DateTime.UtcNow) so tests can fix it.
builder.Services.AddSingleton(TimeProvider.System);

// Database, persistence, accounts and token issuing
// (needs ConnectionStrings:Default and the Jwt section).
builder.Services.AddInfrastructure(builder.Configuration);

// Use cases.
builder.Services.AddScoped<BookingService>();

// Authentication: every request may carry "Authorization: Bearer <token>".
// Validation uses the same Jwt options that JwtTokenService signs with.
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();
builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
    {
        var jwt = jwtOptions.Value;
        // Keep the short JWT claim names ("sub", "role", "name") as issued.
        bearer.MapInboundClaims = false;
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = jwt.SigningKey(),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            NameClaimType = JwtTokenService.NameClaim,
            RoleClaimType = JwtTokenService.RoleClaim,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Errors: application exceptions -> 400/401/403/404/409 problem details;
// anything else -> generic 500 problem details.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApplicationExceptionHandler>();

var app = builder.Build();

// Roles, plus an admin account if Seed:AdminEmail/Seed:AdminPassword are set.
// Requires the database schema to exist (dotnet ef database update).
await IdentitySeeder.SeedAsync(app.Services);

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// Makes Program visible to WebApplicationFactory<Program> in the tests.
public partial class Program;
