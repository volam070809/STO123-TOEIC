using Azure.Identity;
using Azure.Storage.Blobs;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using STO123.Models;
using STO123.Services.Auth;
using STO123.Services.Exam;
using STO123.Services.Knn;
using STO123.Services.Scoring;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

const string frontendCorsPolicy = "Frontend";
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? [];

builder.Services.AddCors(options =>
    options.AddPolicy(frontendCorsPolicy, policy =>
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var key = builder.Configuration["Jwt:Key"];

        if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < 32)
        {
            throw new InvalidOperationException(
                "Jwt:Key must contain at least 32 UTF-8 bytes in User Secrets or an environment variable.");
        }

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],

            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],

            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(key)),

            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,

            NameClaimType = System.Security.Claims.ClaimTypes.NameIdentifier,
            RoleClaimType = System.Security.Claims.ClaimTypes.Role
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddScoped<IPasswordHasher<NguoiDung>, PasswordHasher<NguoiDung>>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IGoogleAuthService, GoogleAuthService>();
builder.Services.AddScoped<IOtpService, OtpService>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<ExamGenerationService>();
builder.Services.AddScoped<ExamAttemptService>();
builder.Services.AddScoped<ExamGradingService>();
builder.Services.AddSingleton<IKnnClassifier, KnnClassifier>();
builder.Services.AddSingleton<KnnDiagnosticsStore>();
builder.Services.AddSingleton<IToeicScoreCalculator, EstimatedLinearToeicScoreCalculator>();

builder.Services.AddDbContext<ToeicDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("ToeicDb")));

var blobAccountName = builder.Configuration["AzureBlob:AccountName"]
    ?? throw new InvalidOperationException(
        "AzureBlob:AccountName is missing.");

builder.Services.AddSingleton(
    new BlobServiceClient(
        new Uri($"https://{blobAccountName}.blob.core.windows.net"),
        new DefaultAzureCredential()));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseCors(frontendCorsPolicy);

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
