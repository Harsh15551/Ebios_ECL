using Ebios.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ebios.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Area> Areas => Set<Area>();
    public DbSet<SubArea> SubAreas => Set<SubArea>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<AttendanceLog> AttendanceLogs => Set<AttendanceLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Area configuration
        modelBuilder.Entity<Area>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.HasIndex(a => a.AreaCode).IsUnique();
            entity.Property(a => a.AreaCode).HasMaxLength(50).IsRequired();
            entity.Property(a => a.AreaName).HasMaxLength(150).IsRequired();
            entity.Property(a => a.Headquarters).HasMaxLength(150);
        });

        // SubArea configuration
        modelBuilder.Entity<SubArea>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.HasIndex(s => s.SubAreaCode).IsUnique();
            entity.Property(s => s.SubAreaCode).HasMaxLength(50).IsRequired();
            entity.Property(s => s.SubAreaName).HasMaxLength(150).IsRequired();

            entity.HasOne(s => s.Area)
                  .WithMany(a => a.SubAreas)
                  .HasForeignKey(s => s.AreaId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        // AppUser configuration
        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.HasKey(u => u.Id);
            entity.HasIndex(u => u.Username).IsUnique();
            entity.Property(u => u.Username).HasMaxLength(50).IsRequired();
            entity.Property(u => u.FullName).HasMaxLength(150);
            entity.Property(u => u.Email).HasMaxLength(150);

            entity.HasOne(u => u.Area)
                  .WithMany()
                  .HasForeignKey(u => u.AreaId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(u => u.SubArea)
                  .WithMany()
                  .HasForeignKey(u => u.SubAreaId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        // Device configuration
        modelBuilder.Entity<Device>(entity =>
        {
            entity.HasKey(d => d.Id);
            entity.HasIndex(d => d.SerialNumber).IsUnique();
            entity.Property(d => d.SerialNumber).HasMaxLength(100).IsRequired();
            entity.Property(d => d.DeviceName).HasMaxLength(150).IsRequired();
            entity.Property(d => d.Direction).HasMaxLength(20).IsRequired();
            entity.Property(d => d.DeviceType).HasMaxLength(50);
            entity.Property(d => d.TimeZone).HasMaxLength(100);

            entity.HasOne(d => d.SubArea)
                  .WithMany()
                  .HasForeignKey(d => d.SubAreaId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Employee configuration
        modelBuilder.Entity<Employee>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.EmpCode).IsUnique();
            entity.Property(e => e.EmpCode).HasMaxLength(50).IsRequired();
            entity.Property(e => e.FullName).HasMaxLength(150).IsRequired();
            entity.Property(e => e.Designation).HasMaxLength(100);

            entity.HasOne(e => e.SubArea)
                  .WithMany()
                  .HasForeignKey(e => e.SubAreaId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // AttendanceLog configuration
        modelBuilder.Entity<AttendanceLog>(entity =>
        {
            entity.HasKey(l => l.Id);
            entity.HasIndex(l => l.EmpCode);
            entity.HasIndex(l => l.LogDate);
            entity.HasIndex(l => l.SerialNumber);
            entity.HasIndex(l => l.Status);
            entity.HasIndex(l => l.IsFlagged);

            entity.Property(l => l.EmpCode).HasMaxLength(50).IsRequired();
            entity.Property(l => l.SerialNumber).HasMaxLength(100).IsRequired();
            entity.Property(l => l.Direction).HasMaxLength(20).IsRequired();
            entity.Property(l => l.Status).HasMaxLength(50).IsRequired();

            entity.HasOne(l => l.Employee)
                  .WithMany()
                  .HasForeignKey(l => l.EmployeeId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(l => l.Device)
                  .WithMany()
                  .HasForeignKey(l => l.DeviceId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(l => l.SubArea)
                  .WithMany()
                  .HasForeignKey(l => l.SubAreaId)
                  .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
