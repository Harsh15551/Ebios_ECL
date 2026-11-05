using Ebios.Application.Interfaces;
using Ebios.Infrastructure.Data;
using Ebios.Infrastructure.EbiosSoap;
using Ebios.Infrastructure.Services;
using Ebios.Web.Services;
using Ebios.Web;
using Ebios.Web.Components;
using Ebios.Web.Endpoints;
using Microsoft.EntityFrameworkCore;

// 0. Load environment secrets from .env file before building configuration
EnvLoader.Load();

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// 1. PostgreSQL Database Configuration
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// 2. Register eBios SOAP Service via HttpClient
builder.Services.AddHttpClient<IEbiosSoapService, EbiosSoapService>();

// 3. Register Application Services (Dependency Injection)
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IAreaService, AreaService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IDeviceService, DeviceService>();
builder.Services.AddScoped<IEmployeeService, EmployeeService>();
builder.Services.AddScoped<IShiftService, ShiftService>();
builder.Services.AddScoped<IRosterService, RosterService>();
builder.Services.AddScoped<IAttendanceProcessingService, AttendanceProcessingService>();
builder.Services.AddScoped<IAttendanceService, AttendanceService>();
builder.Services.AddScoped<IBiometricFormService, BiometricFormService>();
builder.Services.AddScoped<ISftpAttendanceService, SftpAttendanceService>();
builder.Services.AddHostedService<SftpAttendanceSchedulerWorker>();
builder.Services.AddScoped<Ebios.Web.Services.UserSessionService>();

var app = builder.Build();

// 4. Initialize Database & Seed initial ECL data
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<AppDbContext>();
        await DbInitializer.InitializeAsync(context, app.Logger);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Failed to initialize PostgreSQL database during startup.");
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapWebhookEndpoints();
app.MapBiometricEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
