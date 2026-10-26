using Microsoft.EntityFrameworkCore;

namespace ProductionTracker;

public sealed class Device
{
    public int Id { get; set; }
    public string SerialNumber { get; set; } = "";
    public string FirmwareVersion { get; set; } = "";
    public string Status { get; set; } = "Registered";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<TestRecord> Tests { get; set; } = [];
}

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

public sealed class ManufacturingDb(DbContextOptions<ManufacturingDb> options) : DbContext(options)
{
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<TestRecord> Tests => Set<TestRecord>();
    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<Device>().HasIndex(d => d.SerialNumber).IsUnique();
        builder.Entity<Device>().Property(d => d.SerialNumber).HasMaxLength(80).IsRequired();
        builder.Entity<Device>().Property(d => d.FirmwareVersion).HasMaxLength(40).IsRequired();
        builder.Entity<TestRecord>().HasOne(t => t.Device).WithMany(d => d.Tests).HasForeignKey(t => t.DeviceId).OnDelete(DeleteBehavior.Restrict);
    }
}

public record RegisterDeviceRequest(string? SerialNumber, string? FirmwareVersion);

public static class TestSimulation
{
    public static bool Passed(double voltage, double signalQuality) =>
        voltage >= 3.1 && voltage <= 3.5 && signalQuality >= 80;
}
