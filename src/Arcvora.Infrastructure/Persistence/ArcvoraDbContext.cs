using Microsoft.EntityFrameworkCore;

namespace Arcvora.Infrastructure.Persistence;

public sealed class ArcvoraDbContext(DbContextOptions<ArcvoraDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ModelVersion> ModelVersions => Set<ModelVersion>();
    public DbSet<Proposal> Proposals => Set<Proposal>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<Artifact> Artifacts => Set<Artifact>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Email).HasMaxLength(320);
            e.Property(x => x.DisplayName).HasMaxLength(200);
        });

        b.Entity<Workspace>(e => e.Property(x => x.Name).HasMaxLength(200));

        b.Entity<Membership>(e =>
        {
            e.HasKey(x => new { x.WorkspaceId, x.UserId });
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.UserId);
        });

        b.Entity<Project>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(4000);
            e.Property(x => x.TechProfileId).HasMaxLength(50);
            e.Property(x => x.RowVersion).HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
            e.HasIndex(x => x.WorkspaceId);
            e.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ModelVersion>(e =>
        {
            e.Property(x => x.ModelJson).HasColumnType("jsonb");
            e.Property(x => x.ContentHash).HasMaxLength(64);
            e.Property(x => x.Summary).HasMaxLength(500);
            e.Property(x => x.Kind).HasMaxLength(20);
            // Doğrusal geçmiş: aynı numaranın iki kez yazılması veritabanı düzeyinde engellenir.
            e.HasIndex(x => new { x.ProjectId, x.Number }).IsUnique();
            e.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Proposal>(e =>
        {
            e.Property(x => x.Kind).HasMaxLength(20);
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.RequestText).HasMaxLength(8000);
            e.Property(x => x.PatchJson).HasColumnType("jsonb");
            e.Property(x => x.ImpactJson).HasColumnType("jsonb");
            e.Property(x => x.FindingsJson).HasColumnType("jsonb");
            e.Property(x => x.AcceptedGroupsJson).HasColumnType("jsonb");
            e.Property(x => x.Provider).HasMaxLength(100);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.RowVersion).HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
            e.HasIndex(x => new { x.ProjectId, x.CreatedAt });
            e.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Job>(e =>
        {
            e.Property(x => x.Kind).HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.InputJson).HasColumnType("jsonb");
            e.Property(x => x.ProblemsJson).HasColumnType("jsonb");
            e.Property(x => x.ErrorCode).HasMaxLength(60);
            e.Property(x => x.Error).HasMaxLength(2000);
            e.Property(x => x.Stage).HasMaxLength(30);
            e.Property(x => x.LockedBy).HasMaxLength(100);
            e.Property(x => x.IdempotencyKey).HasMaxLength(100);
            e.HasIndex(x => new { x.Status, x.AvailableAt, x.CreatedAt });
            // Aynı idempotency anahtarıyla ikinci iş oluşmaz.
            e.HasIndex(x => new { x.ProjectId, x.IdempotencyKey }).IsUnique().HasFilter("\"IdempotencyKey\" IS NOT NULL");
            e.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Artifact>(e =>
        {
            e.Property(x => x.Format).HasMaxLength(20);
            e.Property(x => x.ProfileId).HasMaxLength(50);
            e.Property(x => x.ProfileVersion).HasMaxLength(20);
            e.Property(x => x.GeneratorVersion).HasMaxLength(50);
            e.Property(x => x.ModelHash).HasMaxLength(64);
            e.Property(x => x.ContentHash).HasMaxLength(64);
            e.Property(x => x.FileName).HasMaxLength(200);
            e.Property(x => x.ContentType).HasMaxLength(100);
            e.HasIndex(x => new { x.ProjectId, x.CreatedAt });
            e.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AuditEntry>(e =>
        {
            e.Property(x => x.Action).HasMaxLength(60);
            e.Property(x => x.ObjectType).HasMaxLength(40);
            e.Property(x => x.ObjectId).HasMaxLength(100);
            e.Property(x => x.DetailJson).HasColumnType("jsonb");
            // Denetim kaydı proje silinse de saklanır; bu yüzden proje FK'sı yoktur.
            e.HasIndex(x => new { x.ProjectId, x.At });
            e.HasIndex(x => new { x.WorkspaceId, x.At });
        });
    }
}
