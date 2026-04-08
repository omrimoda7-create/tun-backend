using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using TunSociety.Api.Configuration;
using TunSociety.Api.Infrastructure;
using TunSociety.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddMemoryCache();
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<AdminAccountOptions>(builder.Configuration.GetSection(AdminAccountOptions.SectionName));
builder.Services.Configure<OllamaOptions>(builder.Configuration.GetSection(OllamaOptions.SectionName));

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Jwt configuration is missing.");

if (string.IsNullOrWhiteSpace(jwtOptions.SigningKey) || jwtOptions.SigningKey.Length < 32)
{
    throw new InvalidOperationException("Jwt:SigningKey must be configured and at least 32 characters long.");
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>();
        if (origins is { Length: > 0 })
        {
            policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
        }
        else
        {
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
        }
    });
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole(RoleNames.Admin));
    options.AddPolicy("ModeratorOrAdmin", policy => policy.RequireRole(RoleNames.Moderator, RoleNames.Admin));
});

builder.Services.AddHttpClient<LocalAiService>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<OllamaOptions>>().Value;
    client.BaseAddress = new Uri($"{options.BaseUrl.TrimEnd('/')}/");
    client.Timeout = TimeSpan.FromSeconds(Math.Max(5, options.TimeoutSeconds));
});
builder.Services.AddScoped<AiScoringClient>();
builder.Services.AddScoped<ModerationService>();
builder.Services.AddScoped<SanctionService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddSingleton<AvatarStorageService>();

var app = builder.Build();

app.UseHttpsRedirection();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
