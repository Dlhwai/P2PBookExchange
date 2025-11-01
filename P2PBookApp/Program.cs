using Npgsql;
using P2PBookApp;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddScoped<NpgsqlConnection>(_ =>
{
    var connStr = builder.Configuration.GetConnectionString("DefaultConnection");
    return new NpgsqlConnection(connStr);
});

builder.Services.AddScoped<P2PBookDB>();
builder.Services.AddScoped<P2PBookLogic>();


var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseExceptionHandler(a => a.Run(async context =>
{
    context.Response.ContentType = "application/json";
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;

    var result = System.Text.Json.JsonSerializer.Serialize(new
    {
        statusCode = 500,
        message = "Internal Server Error"
    });

    await context.Response.WriteAsync(result);
}));

P2PBookAPI.MapEndpoints(app);

app.Run();
