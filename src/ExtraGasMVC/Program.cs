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
using Microsoft.Extensions.Options;

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

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AdminOnly", policy => policy.RequireRole("ADMIN"))
    .AddPolicy("OperadorOrAdmin", policy => policy.RequireRole("ADMIN", "OPERADOR"));

// Bind AuthLockoutOptions desde appsettings (sección "Auth:Lockout").
builder.Services.Configure<AuthLockoutOptions>(
    builder.Configuration.GetSection(AuthLockoutOptions.SectionName));

builder.Services.Configure<PasswordPolicyOptions>(
    builder.Configuration.GetSection(PasswordPolicyOptions.SectionName));

// Bind EmailOptions desde appsettings (seccion "Email"). En produccion las
// credenciales (Username/Password) se setean con dotnet user-secrets — ver README.
builder.Services.Configure<EmailOptions>(
    builder.Configuration.GetSection(EmailOptions.SectionName));

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
builder.Services.AddScoped<IEmailSender, MailKitEmailSender>();
builder.Services.AddScoped<IEmpleadoService, EmpleadoService>();
builder.Services.AddScoped<IRecepcionService, RecepcionService>();
builder.Services.AddScoped<IAuditoriaLoginService, AuditoriaLoginService>();
builder.Services.AddSingleton<IPasswordPolicyService, PasswordPolicyService>();

var app = builder.Build();

// Sanity check de configuracion SMTP fuera de Development: si hay Host pero
// faltan Username/Password, loggear warning y NO crashear (MailKit fallara
// al primer envio; el caller hace fire-and-forget y loggea). En dev (MailHog)
// los campos son opcionales por lo que no se valida.
WarnOnIncompleteSmtpConfig(app);

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
ConfigureForwardedHeaders(app, builder.Configuration);

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

/// <summary>
/// Loggea un warning si en produccion el email tiene Host pero faltan
/// Username/Password. No rompe el startup: el caller hace fire-and-forget
/// y va a loggear el fallo real al primer envio.
/// </summary>
static void WarnOnIncompleteSmtpConfig(WebApplication app)
{
    if (app.Environment.IsDevelopment()) return;

    var emailOptions = app.Services.GetRequiredService<IOptions<EmailOptions>>().Value;
    if (string.IsNullOrWhiteSpace(emailOptions.Host)) return;
    if (!string.IsNullOrEmpty(emailOptions.Username) && !string.IsNullOrEmpty(emailOptions.Password)) return;

    var startupLogger = app.Services.GetRequiredService<ILoggerFactory>()
        .CreateLogger("EmailStartup");
    startupLogger.LogWarning(
        "Email:Host configurado ({Host}) pero Email:Username/Email:Password no estan seteados. " +
        "Defini las credenciales SMTP via 'dotnet user-secrets set' antes de enviar emails. " +
        "Los envios fallaran silenciosamente.",
        emailOptions.Host);
}

/// <summary>
/// Aplica <c>UseForwardedHeaders</c> solo si la sección "ForwardedHeaders"
/// existe en configuración. Si no, no se reescribe nada (defensa contra
/// spoofing de IP).
/// </summary>
static void ConfigureForwardedHeaders(WebApplication app, IConfiguration configuration)
{
    var section = configuration.GetSection("ForwardedHeaders");
    if (!section.Exists()) return;

    var options = new Microsoft.AspNetCore.Builder.ForwardedHeadersOptions
    {
        ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                         | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto,
        RequireHeaderSymmetry = false,
        ForwardLimit = 2,
    };

    AddKnownProxies(options, section.GetSection("KnownProxies"));
    AddKnownNetworks(options, section.GetSection("KnownNetworks"));

    app.UseForwardedHeaders(options);
}

/// <summary>
/// Suma las IPs de <c>KnownProxies</c> al builder de options. Las IPs
/// inválidas se descartan silenciosamente (no son bloqueantes).
/// </summary>
static void AddKnownProxies(
    Microsoft.AspNetCore.Builder.ForwardedHeadersOptions options, IConfigurationSection section)
{
    var proxies = section.Get<string[]>();
    if (proxies is null) return;

    foreach (var ip in proxies)
        if (System.Net.IPAddress.TryParse(ip, out var addr))
            options.KnownProxies.Add(addr);
}

/// <summary>
/// Suma los CIDR de <c>KnownNetworks</c> al builder de options. Los CIDR
/// inválidos se descartan silenciosamente.
/// </summary>
static void AddKnownNetworks(
    Microsoft.AspNetCore.Builder.ForwardedHeadersOptions options, IConfigurationSection section)
{
    var cidrs = section.Get<string[]>();
    if (cidrs is null) return;

    foreach (var cidr in cidrs)
        if (TryParseCidr(cidr, out var network))
            options.KnownIPNetworks.Add(network);
}

/// <summary>
/// Parsea un CIDR ("192.168.0.0/16") a IPNetwork. Usado por la configuracion
/// de ForwardedHeaders.KnownNetworks.
/// </summary>
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
