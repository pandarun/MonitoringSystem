using Microsoft.EntityFrameworkCore;
using MonitoringSystem.Data.Entities;

namespace MonitoringSystem.Data;

/// <summary>
/// Database context for the monitoring system.
/// </summary>
public class MonitoringDbContext : DbContext
{
    public MonitoringDbContext(DbContextOptions<MonitoringDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Gets or sets the sensor states table.
    /// </summary>
    public DbSet<SensorStateEntity> SensorStates => Set<SensorStateEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SensorStateEntity>(entity =>
        {
            entity.ToTable("sensor_states");

            entity.HasKey(e => e.SensorId);

            entity.Property(e => e.SensorId)
                .HasColumnName("sensor_id")
                .HasMaxLength(256)
                .IsRequired();

            entity.Property(e => e.Value)
                .HasColumnName("value")
                .IsRequired();

            entity.Property(e => e.Timestamp)
                .HasColumnName("timestamp")
                .IsRequired();

            entity.Property(e => e.UpdatedAt)
                .HasColumnName("updated_at")
                .IsRequired();

            // Index for querying by timestamp
            entity.HasIndex(e => e.Timestamp);
        });
    }
}
