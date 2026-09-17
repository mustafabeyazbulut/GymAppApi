using System.Text;
using GymAppApi.Application;
using GymAppApi.Infrastructure;
using GymAppApi.Persistence;
using GymAppApi.WebApi.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddHttpContextAccessor();

builder.Services.AddControllers();

var jwtSection = builder.Configuration.GetSection("Jwt");
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Without this, the default inbound claim mapping silently renames the
        // "sub" claim to ClaimTypes.NameIdentifier on the validated
        // ClaimsPrincipal, so every FindFirst(JwtRegisteredClaimNames.Sub)
        // lookup (AuthController, AssignmentsController,
        // AssignmentRoleAuthorizationHandler) returns null — which makes
        // role-gated endpoints fail closed (403) even for correctly
        // authorized callers.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwtSection["Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["SigningKey"]!)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, GymAppApi.WebApi.Authorization.AssignmentRoleAuthorizationHandler>();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("GymAdminOrSuperAdmin", policy => policy.Requirements.Add(
        new GymAppApi.WebApi.Authorization.AssignmentRoleRequirement(
            GymAppApi.Domain.Enums.AssignmentRole.GymAdmin, GymAppApi.Domain.Enums.AssignmentRole.SuperAdmin)));
});

builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("auth", limiterOptions =>
    {
        limiterOptions.PermitLimit = 10;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueLimit = 0;
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseMiddleware<ExceptionMiddleware>();

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseMiddleware<TenantContextMiddleware>();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();

app.Run();

public partial class Program { }
