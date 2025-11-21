using SnapToolCloud.Database;
using SnapToolCloud.Service;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

await SnapToolDB.InitializeAsync();

// Optional: run once on startup to confirm connectivity
await WeatherService.GetRTIOB10ForecastAsync();

await SnapToolService.RunWorkflow();
//await WeatherService.GetLambertWaveDataAsync();

app.Run();
