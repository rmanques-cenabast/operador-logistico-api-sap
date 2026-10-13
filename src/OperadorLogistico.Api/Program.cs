using OperadorLogistico.Infrastructure.Sap;
using System.Reflection;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// Configurar rutas de controladores en minúsculas (lowercase URLs)
builder.Services.AddRouting(options => options.LowercaseUrls = true);

// Configuración de Controllers y Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "Operador Logístico API SAP",
        Version = "v1",
        Description = "API RESTful para integración con SAP vía RFC / NCo"
    });
    
    // Configurar Swagger para leer los comentarios XML de documentación y ejemplos
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }
    
    // Asignar nombres de grupo/etiqueta a los tags de Swagger
    c.TagActionsBy(api => new[] { api.GroupName ?? api.ActionDescriptor.RouteValues["controller"] ?? "SAP" });
    c.DocInclusionPredicate((name, api) => true);

    // Configuración de seguridad para x-api-key en Swagger
    var apiScheme = new OpenApiSecurityScheme
    {
        Description = "Cabecera x-api-key requerida para consumir el puente de SAP",
        Type = SecuritySchemeType.ApiKey,
        Name = "x-api-key",
        In = ParameterLocation.Header,
        Scheme = "ApiKeyScheme"
    };

    c.AddSecurityDefinition("ApiKey", apiScheme);

    c.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecuritySchemeReference("ApiKey", doc),
            new List<string>()
        }
    });
});

// Registro de la Infraestructura SAP pasando la configuración
builder.Services.AddSapInfrastructure(builder.Configuration);

var app = builder.Build();

// Habilitar Swagger
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("./v1/swagger.json", "Operador Logístico API v1");
    c.RoutePrefix = "swagger"; // Swagger disponible en /swagger
});

app.UseHttpsRedirection();

// Middleware para validar x-api-key en .NET
app.Use(async (context, next) =>
{
    // Omitir validación en Swagger, endpoints de salud y prueba de conexión a SAP
    var path = context.Request.Path.Value ?? string.Empty;
    if (path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase) || 
        path == "/" || 
        path == "/health" || 
        path.EndsWith("/probar-conexion", StringComparison.OrdinalIgnoreCase))
    {
        await next();
        return;
    }

    var expectedKey = app.Configuration["Authentication:ApiKey"];
    if (string.IsNullOrEmpty(expectedKey))
    {
        await next();
        return;
    }

    if (!context.Request.Headers.TryGetValue("x-api-key", out var extractedKey) || extractedKey != expectedKey)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { status = "error", message = "Acceso no autorizado." });
        return;
    }

    await next();
});

app.UseAuthorization();
app.MapControllers();

app.Run();
