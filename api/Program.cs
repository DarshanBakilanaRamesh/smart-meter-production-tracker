using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProductionTracker;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<ManufacturingDb>(o => o.UseNpgsql(
    builder.Configuration.GetConnectionString("ManufacturingDb") ??
    "Host=localhost;Database=manufacturing;Username=tracker;Password=local_demo_only"));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }

using (var scope = app.Services.CreateScope())
    await scope.ServiceProvider.GetRequiredService<ManufacturingDb>().Database.EnsureCreatedAsync();

app.MapGet("/health", async (ManufacturingDb db) => await db.Database.CanConnectAsync()
    ? Results.Ok(new { status = "healthy" }) : Results.StatusCode(503));

app.MapGet("/api/summary", async (ManufacturingDb db) =>
{
    int total = await db.Devices.CountAsync();
    int passed = await db.Devices.CountAsync(d => d.Status == "Passed");
    int failed = await db.Devices.CountAsync(d => d.Status == "Failed");
    return Results.Ok(new { total, passed, failed, registered = total - passed - failed, tests = await db.Tests.CountAsync() });
});

app.MapGet("/api/devices", async (ManufacturingDb db, string? status) =>
{
    var query = db.Devices.AsNoTracking().AsQueryable();
    if (!string.IsNullOrEmpty(status)) query = query.Where(d => d.Status == status);
    return Results.Ok(await query.OrderByDescending(d => d.CreatedAt)
        .Select(d => new { d.Id, d.SerialNumber, d.FirmwareVersion, d.Status, d.CreatedAt, testCount = d.Tests.Count })
        .ToListAsync());
});

app.MapGet("/api/devices/{id:int}", async (int id, ManufacturingDb db) =>
{
    var device = await db.Devices.AsNoTracking().Include(d => d.Tests).FirstOrDefaultAsync(d => d.Id == id);
    if (device is null) return Results.NotFound();
    return Results.Ok(new { device.Id, device.SerialNumber, device.FirmwareVersion, device.Status, device.CreatedAt,
        tests = device.Tests.OrderByDescending(t => t.TestedAt).Select(t => new { t.Id, t.TestedAt, t.Voltage, t.SignalQuality, t.Passed, t.Station }) });
});

app.MapPost("/api/devices", async (RegisterDeviceRequest body, ManufacturingDb db) =>
{
    var serial = body.SerialNumber?.Trim().ToUpperInvariant();
    var firmware = body.FirmwareVersion?.Trim();
    if (string.IsNullOrEmpty(serial) || serial.Length > 80 || string.IsNullOrEmpty(firmware) || firmware.Length > 40)
        return Results.BadRequest(new { error = "Serial number (max 80) and firmware version (max 40) are required." });
    if (await db.Devices.AnyAsync(d => d.SerialNumber == serial))
        return Results.Conflict(new { error = "Serial number already registered." });
    var device = new Device { SerialNumber = serial, FirmwareVersion = firmware };
    db.Devices.Add(device);
    try { await db.SaveChangesAsync(); }
    catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
    { return Results.Conflict(new { error = "Serial number already registered." }); }
    return Results.Created($"/api/devices/{device.Id}", new { device.Id, device.SerialNumber, device.FirmwareVersion, device.Status });
});

app.MapPost("/api/devices/{id:int}/tests", async (int id, ManufacturingDb db) =>
{
    var device = await db.Devices.FindAsync(id);
    if (device is null) return Results.NotFound();
    double voltage = Math.Round(3 + Random.Shared.NextDouble() * .6, 3);
    double signal = Math.Round(65 + Random.Shared.NextDouble() * 35, 1);
    var test = new TestRecord { DeviceId = id, Voltage = voltage, SignalQuality = signal,
        Passed = TestSimulation.Passed(voltage, signal) };
    db.Tests.Add(test);
    device.Status = test.Passed ? "Passed" : "Failed";
    await db.SaveChangesAsync();
    return Results.Ok(new { test.Id, test.DeviceId, test.TestedAt, test.Voltage, test.SignalQuality, test.Passed, test.Station });
});

app.Run();
public partial class Program { }
