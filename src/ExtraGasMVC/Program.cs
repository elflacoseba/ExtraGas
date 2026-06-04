using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Mappings;
using ExtraGasMVC.Services.Implementations;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Registrar AutoMapper
builder.Services.AddAutoMapper(typeof(MappingProfile).Assembly);

var connectionString = builder.Configuration.GetConnectionString("ExtraGas")
    ?? throw new InvalidOperationException("Connection string 'ExtraGas' not found.");

builder.Services.AddDbContext<ExtraGasDbContext>(opt =>
    opt.UseMySql(
        connectionString,
        ServerVersion.AutoDetect(connectionString),
        my => my.EnableStringComparisonTranslations()
                 .CommandTimeout(30)
                 .MigrationsAssembly(typeof(Program).Assembly.GetName().Name)));

// Registrar servicios de negocio
builder.Services.AddScoped<IClienteService, ClienteService>();
builder.Services.AddScoped<IPedidoService, PedidoService>();
builder.Services.AddScoped<IProductoService, ProductoService>();
builder.Services.AddScoped<IProveedorService, ProveedorService>();
builder.Services.AddScoped<IPagoService, PagoService>();
builder.Services.AddScoped<IGarrafaService, GarrafaService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
