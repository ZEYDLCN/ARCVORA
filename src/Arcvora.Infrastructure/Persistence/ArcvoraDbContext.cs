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
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<GroupMember> GroupMembers => Set<GroupMember>();
    public DbSet<AppDefinition> Apps => Set<AppDefinition>();
    public DbSet<AppVersion> AppVersions => Set<AppVersion>();
    public DbSet<AppGrant> AppGrants => Set<AppGrant>();
    public DbSet<RequestCounter> RequestCounters => Set<RequestCounter>();
    public DbSet<FlowRequest> FlowRequests => Set<FlowRequest>();
    public DbSet<ApprovalTask> ApprovalTasks => Set<ApprovalTask>();
    public DbSet<RequestEvent> RequestEvents => Set<RequestEvent>();
    public DbSet<AccountingEntry> AccountingEntries => Set<AccountingEntry>();
    public DbSet<AccountingSettings> AccountingSettings => Set<AccountingSettings>();
    public DbSet<Delegation> Delegations => Set<Delegation>();
    public DbSet<EmailMessage> EmailMessages => Set<EmailMessage>();
    public DbSet<NotificationSettings> NotificationSettings => Set<NotificationSettings>();
    public DbSet<RetentionSettings> RetentionSettings => Set<RetentionSettings>();
    public DbSet<UserIdentity> UserIdentities => Set<UserIdentity>();
    public DbSet<Holiday> Holidays => Set<Holiday>();
    public DbSet<LeaveEntry> LeaveEntries => Set<LeaveEntry>();
    public DbSet<DataList> DataLists => Set<DataList>();
    public DbSet<DataListItem> DataListItems => Set<DataListItem>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<AttachmentBlob> AttachmentBlobs => Set<AttachmentBlob>();

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

        b.Entity<Group>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.HasIndex(x => x.WorkspaceId);
            e.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<GroupMember>(e =>
        {
            e.HasKey(x => new { x.GroupId, x.UserId });
            e.HasIndex(x => x.UserId);
            e.HasOne<Group>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AppDefinition>(e =>
        {
            e.ToTable("Apps");
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.Icon).HasMaxLength(30);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.DraftJson).HasColumnType("jsonb");
            e.HasIndex(x => new { x.WorkspaceId, x.Status });
            e.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AppVersion>(e =>
        {
            e.Property(x => x.DefinitionJson).HasColumnType("jsonb");
            e.Property(x => x.Hash).HasMaxLength(64);
            e.HasIndex(x => new { x.AppId, x.Number }).IsUnique();
            e.HasOne<AppDefinition>().WithMany().HasForeignKey(x => x.AppId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AppGrant>(e =>
        {
            e.HasKey(x => new { x.AppId, x.Kind, x.SubjectId });
            e.Property(x => x.Kind).HasMaxLength(10);
            e.HasIndex(x => new { x.Kind, x.SubjectId });
            e.HasOne<AppDefinition>().WithMany().HasForeignKey(x => x.AppId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<RequestCounter>(e =>
        {
            e.HasKey(x => new { x.WorkspaceId, x.Prefix, x.Year });
            e.Property(x => x.Prefix).HasMaxLength(10);
        });

        b.Entity<FlowRequest>(e =>
        {
            e.Property(x => x.Number).HasMaxLength(30);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.DataJson).HasColumnType("jsonb");
            e.Property(x => x.Summary).HasMaxLength(400);
            e.Property(x => x.IdempotencyKey).HasMaxLength(100);
            e.Property(x => x.RowVersion).HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
            e.HasIndex(x => new { x.WorkspaceId, x.Number }).IsUnique();
            e.HasIndex(x => new { x.WorkspaceId, x.RequesterId, x.CreatedAt });
            e.HasIndex(x => new { x.WorkspaceId, x.Status, x.CreatedAt });
            // Aynı istemci anahtarıyla ikinci talep oluşmaz (çift tıklama / ağ tekrarı).
            e.HasIndex(x => new { x.RequesterId, x.IdempotencyKey }).IsUnique().HasFilter("\"IdempotencyKey\" IS NOT NULL");
            e.HasOne<AppDefinition>().WithMany().HasForeignKey(x => x.AppId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ApprovalTask>(e =>
        {
            e.Property(x => x.StepId).HasMaxLength(20);
            e.Property(x => x.StepName).HasMaxLength(100);
            e.Property(x => x.Mode).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Comment).HasMaxLength(1000);
            e.Property(x => x.Fallback).HasMaxLength(40);
            e.HasIndex(x => new { x.AssigneeId, x.Status, x.CreatedAt });
            e.HasIndex(x => new { x.RequestId, x.StepIndex });
            e.HasOne<FlowRequest>().WithMany().HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<RequestEvent>(e =>
        {
            e.Property(x => x.Kind).HasMaxLength(30);
            e.Property(x => x.DetailJson).HasColumnType("jsonb");
            e.HasIndex(x => new { x.RequestId, x.At });
            e.HasOne<FlowRequest>().WithMany().HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AccountingEntry>(e =>
        {
            e.Property(x => x.Number).HasMaxLength(30);
            e.Property(x => x.Category).HasMaxLength(40);
            e.Property(x => x.PayloadJson).HasColumnType("jsonb");
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.LastError).HasMaxLength(500);
            e.Property(x => x.Note).HasMaxLength(500);
            e.Property(x => x.RowVersion).HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
            // Talep başına tek muhasebe kaydı: aynı talep iki kez aktarılmaz.
            e.HasIndex(x => x.RequestId).IsUnique();
            e.HasIndex(x => new { x.WorkspaceId, x.Status, x.CreatedAt });
            e.HasIndex(x => new { x.Status, x.NextAttemptAt });
            e.HasOne<FlowRequest>().WithMany().HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AccountingSettings>(e =>
        {
            e.HasKey(x => x.WorkspaceId);
            e.Property(x => x.WebhookUrl).HasMaxLength(500);
            e.Property(x => x.WebhookSecret).HasMaxLength(200);
            e.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<EmailMessage>(e =>
        {
            e.Property(x => x.ToAddress).HasMaxLength(320);
            e.Property(x => x.ToName).HasMaxLength(200);
            e.Property(x => x.Kind).HasMaxLength(30);
            e.Property(x => x.Subject).HasMaxLength(200);
            e.Property(x => x.DedupeKey).HasMaxLength(120);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.LastError).HasMaxLength(500);
            e.HasIndex(x => x.DedupeKey).IsUnique().HasFilter("\"DedupeKey\" IS NOT NULL");      // aynı olay için ikinci mail yok
            e.HasIndex(x => new { x.Status, x.NextAttemptAt });
            e.HasIndex(x => new { x.WorkspaceId, x.CreatedAt });
            e.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<UserIdentity>(e =>
        {
            e.Property(x => x.Issuer).HasMaxLength(300);
            e.Property(x => x.Subject).HasMaxLength(200);
            e.HasIndex(x => new { x.Issuer, x.Subject }).IsUnique();
            e.HasIndex(x => x.UserId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Holiday>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.HasIndex(x => new { x.WorkspaceId, x.Date }).IsUnique();
            e.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<LeaveEntry>(e =>
        {
            e.Property(x => x.Kind).HasMaxLength(20);
            e.Property(x => x.Days).HasPrecision(7, 2);
            e.Property(x => x.Note).HasMaxLength(200);
            e.Property(x => x.RequestNumber).HasMaxLength(30);
            e.HasIndex(x => new { x.WorkspaceId, x.UserId, x.Year });
            e.HasIndex(x => x.RequestId);
            e.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<DataList>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.NameKey).HasMaxLength(100);
            e.Property(x => x.Description).HasMaxLength(500);
            e.HasIndex(x => new { x.WorkspaceId, x.NameKey }).IsUnique();         // eşzamanlı iki oluşturma aynı adı alamaz
            e.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<DataListItem>(e =>
        {
            e.Property(x => x.Value).HasMaxLength(100);
            e.HasIndex(x => new { x.ListId, x.Sort });
            e.HasOne<DataList>().WithMany().HasForeignKey(x => x.ListId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<RetentionSettings>(e =>
        {
            e.HasKey(x => x.WorkspaceId);
            e.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<NotificationSettings>(e =>
        {
            e.HasKey(x => x.WorkspaceId);
            e.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Delegation>(e =>
        {
            e.Property(x => x.Reason).HasMaxLength(200);
            e.HasIndex(x => new { x.WorkspaceId, x.FromUserId });
            e.HasIndex(x => new { x.ToUserId, x.WorkspaceId });
            e.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Attachment>(e =>
        {
            e.Property(x => x.FieldKey).HasMaxLength(40);
            e.Property(x => x.FileName).HasMaxLength(120);
            e.Property(x => x.ContentType).HasMaxLength(60);
            e.Property(x => x.Sha256).HasMaxLength(64);
            e.HasIndex(x => x.RequestId);
            e.HasIndex(x => new { x.UploadedBy, x.RequestId, x.CreatedAt });
            e.HasOne<AppDefinition>().WithMany().HasForeignKey(x => x.AppId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<FlowRequest>().WithMany().HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AttachmentBlob>(e =>
        {
            e.HasKey(x => x.AttachmentId);
            e.HasOne<Attachment>().WithOne().HasForeignKey<AttachmentBlob>(x => x.AttachmentId).OnDelete(DeleteBehavior.Cascade);
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
