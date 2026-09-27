using Microsoft.EntityFrameworkCore;
using STO123.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddDbContext<ToeicDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("ToeicDb")));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();