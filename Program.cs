using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using PizzaStore.GraphQL;
using PizzaStore.Middleware;
using PizzaStore.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "PizzaStore API",
        Description = "Making the Pizzas you love",
        Version = "v1"
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var connectionString = builder.Configuration.GetConnectionString("Pizzas") ?? "Data Source=Pizzas.db";
builder.Services.AddSqlite<PizzaDb>(connectionString);

var jwtKey = builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("JWT key is not configured.");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "PizzaStore";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "PizzaStoreUsers";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
    };
});

builder.Services.AddAuthorization();

// CORS - allow the frontend application at http://localhost:3000
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowLocal3000", policy =>
    {
        policy.WithOrigins("http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Add GraphQL Server services and register the Query type
builder.Services
    .AddGraphQLServer()
    .AddQueryType<Query>(); // Hot Chocolate auto-discovers methods like 'GetProducts' as fields

var app = builder.Build();

// Global custom middleware: Time Tracking & Exception Handling
app.UseRequestTimeTracking();
app.UseCustomExceptionHandling();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "PizzaStore API V1");
    });

    app.MapGet("/test-exception", () =>
    {
        throw new InvalidOperationException("Test exception triggered to verify ExceptionHandlingMiddleware.");
    });
}

// Enable CORS for the configured frontend before authentication/authorization
app.UseCors("AllowLocal3000");

app.UseAuthentication();
app.UseAuthorization();

// Map the GraphQL HTTP endpoint (Defaults to /graphql)
app.MapGraphQL();

app.MapGet("/", (ILogger<Program> logger) =>
{
    logger.LogInformation("Root endpoint hit");
    return "Hello World!";
});

app.MapGet("/health", (ILogger<Program> logger) =>
{
    logger.LogInformation("Health check endpoint hit");
    return Results.Ok(new { status = "Healthy" });
});

var generateToken = (LoginRequest request, ILogger<Program> logger) =>
{
    logger.LogInformation("Authentication attempt for user '{Username}'", request.Username);

    if (string.IsNullOrWhiteSpace(request.Username) ||
        string.IsNullOrWhiteSpace(request.Password) ||
        !request.Username.Equals("admin", StringComparison.OrdinalIgnoreCase) ||
        !request.Password.Equals("password", StringComparison.OrdinalIgnoreCase))
    {
        logger.LogWarning("Authentication failed for user '{Username}'", request.Username);
        return Results.Unauthorized();
    }

    var claims = new[]
    {
        new Claim(JwtRegisteredClaimNames.Sub, request.Username),
        new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        new Claim(ClaimTypes.Name, request.Username)
    };

    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
    var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

    var token = new JwtSecurityToken(
        issuer: jwtIssuer,
        audience: jwtAudience,
        claims: claims,
        expires: DateTime.UtcNow.AddHours(1),
        signingCredentials: credentials);

    var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

    logger.LogInformation("Authentication succeeded for user '{Username}'", request.Username);

    return Results.Ok(new { token = tokenString });
};

app.MapPost("/token", generateToken)
    .WithName("GenerateToken")
    .WithSummary("Generate JWT token")
    .WithDescription("Authenticates the demo admin user and returns a JWT token for use with the protected pizza endpoints.")
    .WithTags("Authentication");

app.MapPost("/login", generateToken)
    .WithName("Login")
    .WithSummary("Login and generate JWT token")
    .WithTags("Authentication");

app.MapGet("/pizzas", async (PizzaDb db, ILogger<Program> logger) =>
{
    logger.LogInformation("Fetching all pizzas");
    var pizzas = await db.Pizzas.ToListAsync();
    logger.LogInformation("Fetched {Count} pizzas", pizzas.Count);
    return pizzas;
})
    .RequireAuthorization();

app.MapPost("/pizza", async (PizzaDb db, Pizza pizza, ILogger<Program> logger) =>
{
    logger.LogInformation("Creating pizza with name '{Name}'", pizza.Name);
    await db.Pizzas.AddAsync(pizza);
    await db.SaveChangesAsync();
    logger.LogInformation("Created pizza with id {Id}", pizza.Id);
    return Results.Created($"/pizza/{pizza.Id}", pizza);
}).RequireAuthorization();

app.MapGet("/pizza/{id}", async (PizzaDb db, int id, ILogger<Program> logger) =>
{
    logger.LogInformation("Fetching pizza {Id}", id);
    var pizza = await db.Pizzas.FindAsync(id);
    if (pizza is null)
    {
        logger.LogWarning("Pizza {Id} not found", id);
        return Results.NotFound();
    }
    logger.LogInformation("Found pizza {Id}", id);
    return Results.Ok(pizza);
})
    .RequireAuthorization();

app.MapPut("/pizza/{id}", async (PizzaDb db, Pizza updatepizza, int id, ILogger<Program> logger) =>
{
    logger.LogInformation("Updating pizza {Id}", id);
    var pizza = await db.Pizzas.FindAsync(id);
    if (pizza is null)
    {
        logger.LogWarning("Pizza {Id} not found for update", id);
        return Results.NotFound();
    }

    pizza.Name = updatepizza.Name;
    pizza.Description = updatepizza.Description;
    await db.SaveChangesAsync();
    logger.LogInformation("Updated pizza {Id}", id);
    return Results.NoContent();
}).RequireAuthorization();

app.MapDelete("/pizza/{id}", async (PizzaDb db, int id, ILogger<Program> logger) =>
{
    logger.LogInformation("Deleting pizza {Id}", id);
    var pizza = await db.Pizzas.FindAsync(id);
    if (pizza is null)
    {
        logger.LogWarning("Pizza {Id} not found for delete", id);
        return Results.NotFound();
    }

    db.Pizzas.Remove(pizza);
    await db.SaveChangesAsync();
    logger.LogInformation("Deleted pizza {Id}", id);
    return Results.Ok();
}).RequireAuthorization();

app.Run();

public record LoginRequest(string Username, string Password);
