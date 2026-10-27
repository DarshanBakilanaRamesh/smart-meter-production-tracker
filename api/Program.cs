
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProductionTracker;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

// ====================================
// DATABASE CONFIGURATION
// ====================================

builder.Services.AddDbContext<ManufacturingDb>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("ManufacturingDb")
        ?? "Host=localhost;Database=manufacturing;Username=tracker;Password=local_demo_only"
    )
);

// ====================================
// SWAGGER
// ====================================

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// ====================================
// MIDDLEWARE
// ====================================

app.UseDefaultFiles();
app.UseStaticFiles();

// Collect HTTP request metrics
app.UseHttpMetrics();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// ====================================
// DATABASE INITIALIZATION
// ====================================

// Demo initialization.
// Existing Phase 2 tables have already
// been added to PostgreSQL manually.

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider
        .GetRequiredService<ManufacturingDb>();

    await db.Database.EnsureCreatedAsync();
}

// ====================================
// HEALTH CHECK
// ====================================

app.MapGet("/health", async (ManufacturingDb db) =>
{
    bool connected = await db.Database.CanConnectAsync();

    return connected
        ? Results.Ok(new { status = "healthy" })
        : Results.StatusCode(503);
});

// ====================================
// OVERALL PRODUCTION SUMMARY
// ====================================

app.MapGet("/api/summary", async (ManufacturingDb db) =>
{
    int total = await db.Devices.CountAsync();

    int passed = await db.Devices
        .CountAsync(d => d.Status == "Passed");

    int failed = await db.Devices
        .CountAsync(d => d.Status == "Failed");

    int tests = await db.Tests.CountAsync();

    return Results.Ok(new
    {
        total,
        passed,
        failed,
        registered = total - passed - failed,
        tests
    });
});

// ====================================
// GET ALL DEVICES
// ====================================

app.MapGet("/api/devices", async (
    ManufacturingDb db,
    string? status) =>
{
    var query = db.Devices
        .AsNoTracking()
        .AsQueryable();

    if (!string.IsNullOrWhiteSpace(status))
    {
        query = query.Where(d => d.Status == status);
    }

    var devices = await query
        .OrderByDescending(d => d.CreatedAt)
        .Select(d => new
        {
            d.Id,
            d.SerialNumber,
            d.FirmwareVersion,
            d.Status,
            d.CreatedAt,
            d.ProductionOrderId,
            testCount = d.Tests.Count
        })
        .ToListAsync();

    return Results.Ok(devices);
});

// ====================================
// GET DEVICE BY ID
// ====================================

app.MapGet("/api/devices/{id:int}", async (
    int id,
    ManufacturingDb db) =>
{
    var device = await db.Devices
        .AsNoTracking()
        .Include(d => d.Tests)
        .FirstOrDefaultAsync(d => d.Id == id);

    if (device is null)
        return Results.NotFound();

    return Results.Ok(new
    {
        device.Id,
        device.SerialNumber,
        device.FirmwareVersion,
        device.Status,
        device.CreatedAt,
        device.ProductionOrderId,

        tests = device.Tests
            .OrderByDescending(t => t.TestedAt)
            .Select(t => new
            {
                t.Id,
                t.TestedAt,
                t.Voltage,
                t.SignalQuality,
                t.Passed,
                t.Station
            })
    });
});

// ====================================
// REGISTER DEVICE
// ====================================

app.MapPost("/api/devices", async (
    RegisterDeviceRequest request,
    ManufacturingDb db) =>
{
    var serial = request.SerialNumber?
        .Trim()
        .ToUpperInvariant();

    var firmware = request.FirmwareVersion?.Trim();

    if (string.IsNullOrWhiteSpace(serial) ||
        serial.Length > 80 ||
        string.IsNullOrWhiteSpace(firmware) ||
        firmware.Length > 40)
    {
        return Results.BadRequest(new
        {
            error = "Valid serial number and firmware version are required."
        });
    }

    if (await db.Devices.AnyAsync(
        d => d.SerialNumber == serial))
    {
        return Results.Conflict(new
        {
            error = "Serial number already registered."
        });
    }

    var device = new Device
    {
        SerialNumber = serial,
        FirmwareVersion = firmware,
        Status = "Registered"
    };

    db.Devices.Add(device);

    try
    {
        await db.SaveChangesAsync();
    }
    catch (DbUpdateException ex)
        when (ex.InnerException is PostgresException
            { SqlState: "23505" })
    {
        return Results.Conflict(new
        {
            error = "Serial number already registered."
        });
    }

    return Results.Created(
        $"/api/devices/{device.Id}",
        new
        {
            device.Id,
            device.SerialNumber,
            device.FirmwareVersion,
            device.Status
        }
    );
});

// ====================================
// RUN SIMULATED DEVICE TEST
// ====================================

app.MapPost("/api/devices/{id:int}/tests", async (
    int id,
    ManufacturingDb db) =>
{
    var device = await db.Devices.FindAsync(id);

    if (device is null)
        return Results.NotFound();

    double voltage = Math.Round(
        3.0 + Random.Shared.NextDouble() * 0.6, 3);

    double signal = Math.Round(
        65 + Random.Shared.NextDouble() * 35, 1);

    bool passed = TestSimulation.Passed(
        voltage,
        signal
    );

    var test = new TestRecord
    {
        DeviceId = id,
        Voltage = voltage,
        SignalQuality = signal,
        Passed = passed
    };

    db.Tests.Add(test);

    device.Status = passed ? "Passed" : "Failed";

    await db.SaveChangesAsync();

    return Results.Ok(new
    {
        test.Id,
        test.DeviceId,
        test.TestedAt,
        test.Voltage,
        test.SignalQuality,
        test.Passed,
        test.Station
    });
});

// ====================================
// GET ALL PRODUCTION ORDERS
// ====================================

app.MapGet("/api/orders", async (ManufacturingDb db) =>
{
    var orders = await db.ProductionOrders
        .AsNoTracking()
        .OrderByDescending(p => p.CreatedAt)
        .Select(p => new
        {
            p.Id,
            p.OrderNumber,
            p.ProductName,
            p.TargetQuantity,
            p.Status,
            p.CreatedAt,
            RegisteredQuantity = p.Devices.Count
        })
        .ToListAsync();

    return Results.Ok(orders);
});

// ====================================
// GET PRODUCTION ORDER BY ID
// ====================================

app.MapGet("/api/orders/{id:int}", async (
    int id,
    ManufacturingDb db) =>
{
    var order = await db.ProductionOrders
        .AsNoTracking()
        .Where(p => p.Id == id)
        .Select(p => new
        {
            p.Id,
            p.OrderNumber,
            p.ProductName,
            p.TargetQuantity,
            p.Status,
            p.CreatedAt,

            Devices = p.Devices.Select(d => new
            {
                d.Id,
                d.SerialNumber,
                d.FirmwareVersion,
                d.Status
            }).ToList()
        })
        .FirstOrDefaultAsync();

    return order is null
        ? Results.NotFound()
        : Results.Ok(order);
});

// ====================================
// CREATE PRODUCTION ORDER
// ====================================

app.MapPost("/api/orders", async (
    CreateProductionOrderRequest request,
    ManufacturingDb db) =>
{
    var orderNumber = request.OrderNumber?
        .Trim()
        .ToUpperInvariant();

    var productName = request.ProductName?.Trim();

    if (string.IsNullOrWhiteSpace(orderNumber) ||
        orderNumber.Length > 50 ||
        string.IsNullOrWhiteSpace(productName) ||
        productName.Length > 100 ||
        request.TargetQuantity <= 0)
    {
        return Results.BadRequest(new
        {
            error = "Valid order number, product name and positive target quantity are required."
        });
    }

    if (await db.ProductionOrders.AnyAsync(
        p => p.OrderNumber == orderNumber))
    {
        return Results.Conflict(new
        {
            error = "Production order already exists."
        });
    }

    var order = new ProductionOrder
    {
        OrderNumber = orderNumber,
        ProductName = productName,
        TargetQuantity = request.TargetQuantity,
        Status = "Planned"
    };

    db.ProductionOrders.Add(order);

    try
    {
        await db.SaveChangesAsync();
    }
    catch (DbUpdateException ex)
        when (ex.InnerException is PostgresException
            { SqlState: "23505" })
    {
        return Results.Conflict(new
        {
            error = "Production order already exists."
        });
    }

    return Results.Created(
        $"/api/orders/{order.Id}",
        new
        {
            order.Id,
            order.OrderNumber,
            order.ProductName,
            order.TargetQuantity,
            order.Status,
            order.CreatedAt
        }
    );
});

// ====================================
// ASSIGN DEVICE TO PRODUCTION ORDER
// ====================================

app.MapPost("/api/orders/{id:int}/devices", async (
    int id,
    AddDeviceToOrderRequest request,
    ManufacturingDb db) =>
{
    var order = await db.ProductionOrders
        .Include(p => p.Devices)
        .FirstOrDefaultAsync(p => p.Id == id);

    if (order is null)
    {
        return Results.NotFound(new
        {
            error = "Production order not found."
        });
    }

    if (order.Status == "Completed")
    {
        return Results.Conflict(new
        {
            error = "Production order already completed."
        });
    }

    if (order.Devices.Count >= order.TargetQuantity)
    {
        return Results.Conflict(new
        {
            error = "Target quantity already reached."
        });
    }

    var serial = request.SerialNumber?
        .Trim()
        .ToUpperInvariant();

    var firmware = request.FirmwareVersion?.Trim();

    if (string.IsNullOrWhiteSpace(serial) ||
        serial.Length > 80 ||
        string.IsNullOrWhiteSpace(firmware) ||
        firmware.Length > 40)
    {
        return Results.BadRequest(new
        {
            error = "Valid serial number and firmware version are required."
        });
    }

    if (await db.Devices.AnyAsync(
        d => d.SerialNumber == serial))
    {
        return Results.Conflict(new
        {
            error = "Device serial number already exists."
        });
    }

    var device = new Device
    {
        SerialNumber = serial,
        FirmwareVersion = firmware,
        ProductionOrderId = order.Id,
        Status = "Registered"
    };

    db.Devices.Add(device);

    order.Status = "InProgress";

    try
    {
        await db.SaveChangesAsync();
    }
    catch (DbUpdateException ex)
        when (ex.InnerException is PostgresException
            { SqlState: "23505" })
    {
        return Results.Conflict(new
        {
            error = "Device serial number already exists."
        });
    }

    return Results.Created(
        $"/api/devices/{device.Id}",
        new
        {
            device.Id,
            device.SerialNumber,
            device.FirmwareVersion,
            device.ProductionOrderId,
            device.Status
        }
    );
});

// ====================================
// PRODUCTION PROGRESS ANALYTICS
// ====================================

app.MapGet("/api/orders/{id:int}/progress", async (
    int id,
    ManufacturingDb db) =>
{
    var order = await db.ProductionOrders
        .AsNoTracking()
        .Where(p => p.Id == id)
        .Select(p => new
        {
            p.Id,
            p.OrderNumber,
            p.ProductName,
            p.TargetQuantity,
            p.Status,

            Registered = p.Devices.Count(),

            Passed = p.Devices.Count(
                d => d.Status == "Passed"),

            Failed = p.Devices.Count(
                d => d.Status == "Failed"),

            Pending = p.Devices.Count(
                d => d.Status == "Registered")
        })
        .FirstOrDefaultAsync();

    if (order is null)
    {
        return Results.NotFound(new
        {
            error = "Production order not found."
        });
    }

    double registrationProgress = Math.Round(
        (double)order.Registered /
        order.TargetQuantity * 100, 2);

    double productionProgress = Math.Round(
        (double)order.Passed /
        order.TargetQuantity * 100, 2);

    int testedDevices = order.Passed + order.Failed;

    double passRate = testedDevices > 0
        ? Math.Round(
            (double)order.Passed /
            testedDevices * 100, 2)
        : 0;

    return Results.Ok(new
    {
        order.Id,
        order.OrderNumber,
        order.ProductName,
        order.TargetQuantity,
        order.Status,

        order.Registered,
        order.Passed,
        order.Failed,
        order.Pending,

        RegistrationProgress = registrationProgress,
        ProductionProgress = productionProgress,
        CurrentPassRate = passRate
    });
});

// ====================================
// PROMETHEUS METRICS ENDPOINT
// ====================================

app.MapMetrics();

// ====================================
// START APPLICATION
// ====================================

app.Run();

public partial class Program { }
