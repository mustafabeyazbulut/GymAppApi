using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Localization;
using GymAppApi.Application;
using GymAppApi.Infrastructure;
using GymAppApi.Persistence;
using GymAppApi.WebApi.BackgroundServices;
using GymAppApi.WebApi.Middleware;
using GymAppApi.WebApi.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddHostedService<MembershipExpiryReminderHostedService>();
builder.Services.AddHostedService<OutstandingBalanceReminderHostedService>();

// AddStaffMemberCommand.Role is the first request body to expose an enum to
// clients - without this, "role":"Member" fails model binding since
// System.Text.Json defaults to numeric enum (de)serialization.
builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddCors(options =>
{
    options.AddPolicy("DevClients", policy => policy
        .AllowAnyOrigin()
        .AllowAnyHeader()
        .AllowAnyMethod());
});

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
    options.AddPolicy("SuperAdminOnly", policy => policy.Requirements.Add(
        new GymAppApi.WebApi.Authorization.AssignmentRoleRequirement(
            GymAppApi.Domain.Enums.AssignmentRole.SuperAdmin)));
    options.AddPolicy("StaffManagement", policy => policy.Requirements.Add(
        new GymAppApi.WebApi.Authorization.AssignmentRoleRequirement(
            GymAppApi.Domain.Enums.AssignmentRole.BranchManager,
            GymAppApi.Domain.Enums.AssignmentRole.GymAdmin,
            GymAppApi.Domain.Enums.AssignmentRole.SuperAdmin)));
});

// IP bazlı "auth" politikası + OTP/kod uçlarında telefon/tanımlayıcı bazlı
// ek sınır; 429 yanıtı yerelleştirilmiş gövde ve Retry-After ile döner (bkz.
// RateLimiting/AuthRateLimiting.cs).
builder.Services.AddAuthRateLimiting(builder.Configuration);

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseCors("DevClients");
}

app.UseMiddleware<ExceptionMiddleware>();

// Mobil, uygulama icinde secili olan dili (tr/en) her istekte standart
// Accept-Language header'i ile gonderiyor - CurrentUICulture bu middleware
// tarafindan ayarlaniyor; hem ExceptionMiddleware'in (yukarida, bu yuzden
// onu SARMALIYOR - middleware sirasinda ExceptionMiddleware'in try/catch'i
// bunu da kapsamali) hem FluentValidation'in varsayilan mesajlarinin
// (kendisi de CurrentUICulture'a bakar) otomatik olarak dogru dili
// kullanmasini sagliyor. Desteklenmeyen/eksik bir dil -> varsayilan
// Ingilizce (uygulamanin standart dili).
var supportedCultures = new[] { new CultureInfo("en"), new CultureInfo("tr") };
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("en"),
    SupportedCultures = supportedCultures,
    SupportedUICultures = supportedCultures,
});

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseMiddleware<TenantContextMiddleware>();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();

app.Run();

public partial class Program { }
