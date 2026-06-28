using Microsoft.EntityFrameworkCore;
using QualityGateService.Models;

namespace QualityGateService.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Finding> Findings => Set<Finding>();
    public DbSet<QualityGateResult> QualityGateResults => Set<QualityGateResult>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Finding>(entity =>
        {
            entity.HasKey(finding => finding.Id);
            entity.Property(finding => finding.Title).HasMaxLength(256).IsRequired();
            entity.Property(finding => finding.Description).HasMaxLength(2048);
            entity.Property(finding => finding.Severity).HasConversion<string>().HasMaxLength(32);
            entity.Property(finding => finding.RuleId).HasMaxLength(256);
            entity.Property(finding => finding.FilePath).HasMaxLength(1024);
            entity.Property(finding => finding.Recommendation).HasMaxLength(2048);
            entity.Property(finding => finding.CvssScore).HasPrecision(4, 1);
            entity.Property(finding => finding.CvssVector).HasMaxLength(512);
            entity.Property(finding => finding.CweId).HasMaxLength(64);
            entity.Property(finding => finding.CveId).HasMaxLength(64);
            entity.Property(finding => finding.Tool).HasConversion<string>().HasMaxLength(64);
            entity.HasIndex(finding => finding.ScanId);
        });

        modelBuilder.Entity<QualityGateResult>(entity =>
        {
            entity.HasKey(result => result.Id);
            entity.Property(result => result.Verdict).HasMaxLength(32).IsRequired();
            entity.OwnsOne(result => result.Summary);
            entity.Ignore(result => result.Results);
            entity.Ignore(result => result.Action);
            entity.Ignore(result => result.Passed);
            entity.Ignore(result => result.RollbackExecuted);
            entity.Ignore(result => result.RollbackMessage);
            entity.Property(result => result.DeploymentId).HasMaxLength(256);
            entity.HasIndex(result => result.ScanId);
            entity.HasIndex(result => result.DeploymentId);
        });
    }
}
