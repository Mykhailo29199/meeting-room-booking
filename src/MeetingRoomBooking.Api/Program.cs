using MeetingRoomBooking.Api.Errors;
using MeetingRoomBooking.Api.OpenApi;
using MeetingRoomBooking.Api.Realtime;
using MeetingRoomBooking.Application.Bookings;
using MeetingRoomBooking.Application.Resources;
using MeetingRoomBooking.Infrastructure;
using MeetingRoomBooking.Infrastructure.Identity;
using MeetingRoomBooking.Infrastructure.Persistence;
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
builder.Services.AddScoped<BookingListService>();
builder.Services.AddScoped<ResourceService>();

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
        // Browsers cannot set the Authorization header on a WebSocket, so the
        // SignalR client sends the token as ?access_token=... — accepted on the
        // hub path only, never on the REST API.
        bearer.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var token = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments(ScheduleHub.Path))
                    context.Token = token;
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

// Real-time schedule updates (task item 7). With Azure:SignalR:ConnectionString
// set (in Azure), connections go through Azure SignalR Service; without it
// (locally, in tests) the app hosts SignalR itself — same hub, same code.
var signalR = builder.Services.AddSignalR();
var azureSignalR = builder.Configuration["Azure:SignalR:ConnectionString"];
if (!string.IsNullOrWhiteSpace(azureSignalR))
    signalR.AddAzureSignalR(azureSignalR);
builder.Services.AddSingleton<IScheduleNotifier, SignalRScheduleNotifier>();

builder.Services.AddControllers();

// OpenAPI description (/openapi/v1.json), including bearer-token security,
// shown by Swagger UI at /swagger.
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerSecurityTransformer>();
    options.AddOperationTransformer<BearerSecurityTransformer>();
});

// Errors: application exceptions -> 400/401/403/404/409 problem details;
// anything else -> generic 500 problem details.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApplicationExceptionHandler>();

var app = builder.Build();

// In Azure (Database:MigrateOnStartup=true) the app brings its own schema up
// to date, so a deployment needs no manual database step. Safe there because
// the free App Service plan runs a single instance: no two instances migrate
// at once. Elsewhere the schema comes from `dotnet ef database update`.
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
    await DatabaseMigrator.MigrateAsync(app.Services);

// Roles, plus an admin account if Seed:AdminEmail/Seed:AdminPassword are set.
// Requires the database schema to exist (see above).
await IdentitySeeder.SeedAsync(app.Services);

app.UseExceptionHandler();
app.UseStatusCodePages();

// API docs and Swagger UI in every environment, the deployed app included, so
// a reviewer can try the API there. They reveal no data: every endpoint
// except register and login still needs a token.
app.MapOpenApi();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/openapi/v1.json", "Meeting Room Booking API");
    options.DocumentTitle = "Meeting Room Booking API";
});

// Not in Development: the Angular dev server proxies /api and /hubs to the
// API's plain-HTTP port, and a redirect to the HTTPS port would send the
// browser to another origin, where the request fails CORS. Elsewhere HSTS
// tells browsers to use HTTPS only. In Azure, TLS ends at App Service's
// front end; ASPNETCORE_FORWARDEDHEADERS_ENABLED=true (an app setting) lets
// the app see that the request came over HTTPS.
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

// The Angular client, copied into wwwroot when the app is published: its
// files here, and index.html for "/" and its routes by the fallback below.
// One origin for the client, the API and the hub, so no CORS. Locally
// wwwroot is empty and the Angular dev server serves the client.
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<ScheduleHub>(ScheduleHub.Path);

// Any other path is a client route (/resources/..., /my-bookings): the
// client's index.html, whose router takes over. Unknown API and hub paths
// stay 404 problem details, so a wrong API URL never looks like a page.
app.MapFallback("/api/{**path}", () => Results.NotFound());
app.MapFallback("/hubs/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

app.Run();

// Makes Program visible to WebApplicationFactory<Program> in the tests.
public partial class Program;
