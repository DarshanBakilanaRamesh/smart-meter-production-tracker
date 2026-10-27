
using Microsoft.EntityFrameworkCore;

namespace ProductionTracker;

// DEVICE MODEL

public sealed class Device
{
    public int Id { get; set; }

    public string SerialNumber { get; set; } = "";

    public string FirmwareVersion { get; set; } = "";

    public string Status { get; set; } = "Registered";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<TestRecord> Tests { get; set; } = [];

    // Production Order relationship
    public int? ProductionOrderId { get; set; }

    public ProductionOrder? ProductionOrder { get; set; }
}

// PRODUCTION ORDER MODEL

public sealed class ProductionOrder
{
    public int Id { get; set; }

    public string OrderNumber { get; set; } = "";

    public string ProductName { get; set; } = "";

    public int TargetQuantity { get; set; }

    public string Status { get; set; } = "Planned";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<Device> Devices { get; set; } = [];
}

// TEST RECORD MODEL

public sealed class TestRecord
{
    public int Id { get; set; }

    public int DeviceId { get; set; }

    public Device Device { get; set; } = null!;

    public DateTime TestedAt { get; set; } = DateTime.UtcNow;

    public double Voltage { get; set; }

    public double SignalQuality { get; set; }

    public bool Passed { get; set; }

    public string Station { get; set; } = "SIM-01";
}

// DATABASE CONTEXT

public sealed class ManufacturingDb(
    DbContextOptions<ManufacturingDb> options
) : DbContext(options)
{
    public DbSet<Device> Devices => Set<Device>();

    public DbSet<TestRecord> Tests => Set<TestRecord>();

    public DbSet<ProductionOrder> ProductionOrders
        => Set<ProductionOrder>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Device configuration
        builder.Entity<Device>()
            .HasIndex(d => d.SerialNumber)
            .IsUnique();

        builder.Entity<Device>()
            .Property(d => d.SerialNumber)
            .HasMaxLength(80)
            .IsRequired();

        builder.Entity<Device>()
            .Property(d => d.FirmwareVersion)
            .HasMaxLength(40)
            .IsRequired();

        // Device -> Test Records
        builder.Entity<TestRecord>()
            .HasOne(t => t.Device)
            .WithMany(d => d.Tests)
            .HasForeignKey(t => t.DeviceId)
            .OnDelete(DeleteBehavior.Restrict);

        // Production Order configuration
        builder.Entity<ProductionOrder>()
            .HasIndex(p => p.OrderNumber)
            .IsUnique();

        builder.Entity<ProductionOrder>()
            .Property(p => p.OrderNumber)
            .HasMaxLength(50)
            .IsRequired();

        builder.Entity<ProductionOrder>()
            .Property(p => p.ProductName)
            .HasMaxLength(100)
            .IsRequired();

        // Production Order -> Devices
        builder.Entity<Device>()
            .HasOne(d => d.ProductionOrder)
            .WithMany(p => p.Devices)
            .HasForeignKey(d => d.ProductionOrderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

// API REQUEST MODELS

public record RegisterDeviceRequest(
    string? SerialNumber,
    string? FirmwareVersion
);

public record CreateProductionOrderRequest(
    string? OrderNumber,
    string? ProductName,
    int TargetQuantity
);

public record AddDeviceToOrderRequest(
    string? SerialNumber,
    string? FirmwareVersion
);

// SIMULATED HARDWARE TEST

public static class TestSimulation
{
    public static bool Passed(
        double voltage,
        double signalQuality)
    {
        // Demo thresholds, not real device specifications
        return voltage >= 3.1 &&
               voltage <= 3.5 &&
               signalQuality >= 80;
    }
}
