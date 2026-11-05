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
    public DbSet<ShiftMaster> ShiftMasters => Set<ShiftMaster>();
    public DbSet<EmployeeShiftRoster> EmployeeShiftRosters => Set<EmployeeShiftRoster>();
    public DbSet<AttendanceDailySummary> AttendanceDailySummaries => Set<AttendanceDailySummary>();
    public DbSet<DesignationMaster> DesignationMasters => Set<DesignationMaster>();
    public DbSet<EmployeeBiometricForm> EmployeeBiometricForms => Set<EmployeeBiometricForm>();

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
                  .IsRequired(false)
                  .OnDelete(DeleteBehavior.SetNull);
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

        // ShiftMaster configuration
        modelBuilder.Entity<ShiftMaster>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.HasIndex(s => s.ShiftCode).IsUnique();
            entity.Property(s => s.ShiftCode).HasMaxLength(50).IsRequired();
            entity.Property(s => s.ShiftName).HasMaxLength(150).IsRequired();
        });

        // EmployeeShiftRoster configuration
        modelBuilder.Entity<EmployeeShiftRoster>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.HasIndex(r => new { r.EmpCode, r.RosterDate }).IsUnique();
            entity.Property(r => r.EmpCode).HasMaxLength(50).IsRequired();
            entity.Property(r => r.ShiftCode).HasMaxLength(50).IsRequired();

            entity.HasOne(r => r.Employee)
                  .WithMany()
                  .HasForeignKey(r => r.EmployeeId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(r => r.Shift)
                  .WithMany()
                  .HasForeignKey(r => r.ShiftId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        // AttendanceDailySummary configuration
        modelBuilder.Entity<AttendanceDailySummary>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.HasIndex(s => new { s.EmpCode, s.AttendanceDate }).IsUnique();
            entity.HasIndex(s => s.AttendanceDate);
            entity.HasIndex(s => s.Status);
            entity.HasIndex(s => s.SubAreaId);

            entity.Property(s => s.EmpCode).HasMaxLength(50).IsRequired();
            entity.Property(s => s.ShiftCode).HasMaxLength(50).IsRequired();
            entity.Property(s => s.Status).HasMaxLength(50).IsRequired();
            entity.Property(s => s.RosterStatus).HasMaxLength(50).IsRequired();

            entity.HasOne(s => s.Employee)
                  .WithMany()
                  .HasForeignKey(s => s.EmployeeId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(s => s.SubArea)
                  .WithMany()
                  .HasForeignKey(s => s.SubAreaId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(s => s.Shift)
                  .WithMany()
                  .HasForeignKey(s => s.ShiftId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        // EmployeeBiometricForm configuration (ECL & WEBEL registration)
        modelBuilder.Entity<EmployeeBiometricForm>(entity =>
        {
            entity.HasKey(f => f.Id);
            entity.HasIndex(f => f.EmpCode).IsUnique();
            entity.Property(f => f.EmpCode).HasMaxLength(50).IsRequired();
            entity.Property(f => f.EmployeeName).HasMaxLength(150);
            entity.Property(f => f.AadhaarNumber).HasMaxLength(50);
            entity.Property(f => f.Category).HasMaxLength(50);
            entity.Property(f => f.PositionDesignation).HasMaxLength(100);
            entity.Property(f => f.DepartmentUnit).HasMaxLength(150);
            entity.Property(f => f.PlaceOfPosting).HasMaxLength(150);
            entity.Property(f => f.RegistrationLocation).HasMaxLength(150);
            entity.Property(f => f.ControllingOfficerName).HasMaxLength(150);
            entity.Property(f => f.ControllingOfficerDesignation).HasMaxLength(150);

            entity.Property(f => f.AadhaarPhotoPath).HasMaxLength(300);
            entity.Property(f => f.IdCardPhotoPath).HasMaxLength(300);
            entity.Property(f => f.PersonPhotoPath).HasMaxLength(300);
            entity.Property(f => f.TemplatePhotoPath).HasMaxLength(300);
            entity.Property(f => f.SignedPhysicalFormPath).HasMaxLength(300);

            entity.Property(f => f.EclHrName).HasMaxLength(150);
            entity.Property(f => f.EclHrDesignation).HasMaxLength(150);
            entity.Property(f => f.EclHrDepartment).HasMaxLength(100);
            entity.Property(f => f.EclHrOrganization).HasMaxLength(100);

            entity.Property(f => f.EclSecurityName).HasMaxLength(150);
            entity.Property(f => f.EclSecurityDesignation).HasMaxLength(150);
            entity.Property(f => f.EclSecurityDepartment).HasMaxLength(100);
            entity.Property(f => f.EclSecurityOrganization).HasMaxLength(100);

            entity.Property(f => f.WebelName).HasMaxLength(150);
            entity.Property(f => f.WebelDesignation).HasMaxLength(150);
            entity.Property(f => f.WebelDepartment).HasMaxLength(100);
            entity.Property(f => f.WebelOrganization).HasMaxLength(100);

            entity.Property(f => f.Witness4Name).HasMaxLength(150);
            entity.Property(f => f.Witness4Designation).HasMaxLength(150);
            entity.Property(f => f.Witness4Department).HasMaxLength(100);
            entity.Property(f => f.Witness4Organization).HasMaxLength(100);

            entity.HasOne(f => f.Employee)
                  .WithMany()
                  .HasForeignKey(f => f.EmployeeId)
                  .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
