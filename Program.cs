using Npgsql;
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

//var configs = new[]
//{
//    ("Berth 02","180k","MC1","A11N","A8N","A6N","A3N"),
//    ("Berth 02","180k","MC2","A11N","A9N","A6N","A3N"),
//    ("Berth 03","180k","MC1","A11S","A8S","A6S","A3S"),
//    ("Berth 03","180k","MC2","A11S","A9S","A6S","A3S"),
//    ("Berth 04","180k","MC1","A18N","A15N","A14N","A11N"),
//    ("Berth 04","180k","MC2","A18N","A16N","A14N","A11N"),
//    ("Berth 04","210k","MC1","A18N","A16N","A14N","A11N"),
//    ("Berth 04","210k","MC2","A18N","A16N","A14N","A11N"),
//    ("Berth 05","180k","MC1","A18S","A15S","A14S","A11S"),
//    ("Berth 05","180k","MC2","A18S","A16S","A14S","A11S"),
//    ("Berth 05","210k","MC1","A18S","A16S","A14S","A11S"),
//    ("Berth 05","210k","MC2","A18S","A16S","A14S","A11S"),
//};

//using var conn = new NpgsqlConnection(SnapToolDB.ConnectionString);
//await conn.OpenAsync();
//using var tx = await conn.BeginTransactionAsync();

//await SnapToolDB.BulkInsertMooringConfigsAsync(conn, tx, configs);

//await tx.CommitAsync();

// Optional: run once on startup to confirm connectivity
await WeatherService.GetRTIOB10ForecastAsync();

await SnapToolService.RunWorkflow();
//await WeatherService.GetLambertWaveDataAsync();

app.Run();
