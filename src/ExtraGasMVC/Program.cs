using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using ExtraGasMVC.Configuration;
using ExtraGasMVC.Data.Context;
using ExtraGasMVC.Mappings;
using ExtraGasMVC.Services.Implementations;
using ExtraGasMVC.Services.Interfaces;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddMemoryCache();
var mvcBuilder = builder.Services.AddControllersWithViews();
if (builder.Environment.IsDevelopment())
{
    mvcBuilder.AddRazorRuntimeCompilation();
}

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("ADMIN"));
    options.AddPolicy("OperadorOrAdmin", policy => policy.RequireRole("ADMIN", "OPERADOR"));
});

// Bind AuthLockoutOptions desde appsettings (sección "Auth:Lockout").
builder.Services.Configure<AuthLockoutOptions>(
    builder.Configuration.GetSection(AuthLockoutOptions.SectionName));

builder.Services.Configure<PasswordPolicyOptions>(
    builder.Configuration.GetSection(PasswordPolicyOptions.SectionName));

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
builder.Services.AddScoped<IUsuarioService, UsuarioService>();
builder.Services.AddScoped<IEmpleadoService, EmpleadoService>();
builder.Services.AddScoped<IRecepcionService, RecepcionService>();
builder.Services.AddScoped<IAuditoriaLoginService, AuditoriaLoginService>();
builder.Services.AddSingleton<IPasswordPolicyService, PasswordPolicyService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days.
    app.UseHsts();
}

// Soporte para reverse proxies (Caddy, nginx, IIS, Cloudflare): el middleware
// reescribe HttpContext.Connection.RemoteIpAddress con el primer IP de
// X-Forwarded-For cuando el request proviene de un proxy/red confiable.
// Solo se aplica si hay al menos un KnownProxy/Network configurado; sin
// ninguno, ASP.NET falla a "closed" y no se reescribe nada (defensa contra
// spoofing de IP). Configurar via appsettings:
//   "ForwardedHeaders": {
//     "KnownProxies":  ["10.0.0.5", "192.168.1.20"],
//     "KnownNetworks": ["10.0.0.0/8", "192.168.0.0/16", "172.16.0.0/12"]
//   }
var forwardedHeadersSection = builder.Configuration.GetSection("ForwardedHeaders");
var hasForwardedConfig = forwardedHeadersSection.Exists();
if (hasForwardedConfig)
{
    var forwardedOptions = new Microsoft.AspNetCore.Builder.ForwardedHeadersOptions
    {
        ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                         | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto,
        RequireHeaderSymmetry = false,
        ForwardLimit = 2,
    };

    var knownProxies = forwardedHeadersSection.GetSection("KnownProxies").Get<string[]>();
    if (knownProxies is not null)
        foreach (var ip in knownProxies)
            if (System.Net.IPAddress.TryParse(ip, out var addr))
                forwardedOptions.KnownProxies.Add(addr);

    var knownNetworks = forwardedHeadersSection.GetSection("KnownNetworks").Get<string[]>();
    if (knownNetworks is not null)
        foreach (var cidr in knownNetworks)
            if (TryParseCidr(cidr, out var network))
                forwardedOptions.KnownIPNetworks.Add(network);

    app.UseForwardedHeaders(forwardedOptions);
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();

// Parsea un CIDR ("192.168.0.0/16") a IPNetwork. Usado por la configuracion
// de ForwardedHeaders.KnownNetworks.
static bool TryParseCidr(string cidr, out IPNetwork network)
{
    network = default;
    var parts = cidr.Split('/');
    if (parts.Length != 2) return false;
    if (!IPAddress.TryParse(parts[0], out var prefix)) return false;
    if (!int.TryParse(parts[1], out var bits)) return false;
    var max = prefix.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
    if (bits < 0 || bits > max) return false;
    try
    {
        network = IPNetwork.Parse(cidr);
        return true;
    }
    catch
    {
        return false;
    }
}
