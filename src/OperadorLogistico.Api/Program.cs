using OperadorLogistico.Infrastructure.Sap;
using System.Reflection;

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
});

// Registro de la Infraestructura SAP pasando la configuración
builder.Services.AddSapInfrastructure(builder.Configuration);

var app = builder.Build();

// Habilitar Swagger
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Operador Logístico API v1");
    c.RoutePrefix = "swagger"; // Swagger disponible en /swagger
});

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
