using System.Reflection;
using GymAppApi.Application.Common.Interfaces;
using GymAppApi.Domain.Common;
using GymAppApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GymAppApi.Persistence.Context;

public class GymAppApiDbContext : DbContext
{
    private static readonly HashSet<Type> IntentionallyUnscopedEntityTypes = new()
    {
        typeof(User),
        typeof(OtpVerification),
        typeof(DeviceToken),
        typeof(RefreshToken),
        typeof(PendingContactVerification),
        typeof(Notification),
        typeof(PendingAssignmentInvitation),
    };

    private readonly ITenantContext _tenantContext;

    public GymAppApiDbContext(DbContextOptions<GymAppApiDbContext> options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<OtpVerification> OtpVerifications => Set<OtpVerification>();
    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<PendingContactVerification> PendingContactVerifications => Set<PendingContactVerification>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<PendingAssignmentInvitation> PendingAssignmentInvitations => Set<PendingAssignmentInvitation>();
    public DbSet<Package> Packages => Set<Package>();
    public DbSet<PackageAssignment> PackageAssignments => Set<PackageAssignment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        ApplyGlobalQueryFilters(modelBuilder);
    }

    private void ApplyGlobalQueryFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;

            if (typeof(Company).IsAssignableFrom(clrType))
            {
                var method = GetType().GetMethod(nameof(SetCompanySelfFilter), BindingFlags.NonPublic | BindingFlags.Instance)!
                    .MakeGenericMethod(clrType);
                method.Invoke(this, new object[] { modelBuilder });
            }
            else if (typeof(ICompanyScoped).IsAssignableFrom(clrType) && typeof(IDeactivatable).IsAssignableFrom(clrType))
            {
                var method = GetType().GetMethod(nameof(SetCompanyScopedDeactivatableFilter), BindingFlags.NonPublic | BindingFlags.Instance)!
                    .MakeGenericMethod(clrType);
                method.Invoke(this, new object[] { modelBuilder });
            }
            else if (typeof(ICompanyScoped).IsAssignableFrom(clrType))
            {
                var method = GetType().GetMethod(nameof(SetCompanyScopedFilter), BindingFlags.NonPublic | BindingFlags.Instance)!
                    .MakeGenericMethod(clrType);
                method.Invoke(this, new object[] { modelBuilder });
            }
            else if (typeof(ITenantScoped).IsAssignableFrom(clrType))
            {
                var method = GetType().GetMethod(nameof(SetNullableTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!
                    .MakeGenericMethod(clrType);
                method.Invoke(this, new object[] { modelBuilder });
            }
            else if (!IntentionallyUnscopedEntityTypes.Contains(clrType))
            {
                throw new InvalidOperationException(
                    $"Entity type '{clrType.Name}' does not implement any tenant-scoping marker interface " +
                    "(ICompanyScoped, ITenantScoped, or IDeactivatable on Company) and is not listed in " +
                    $"{nameof(IntentionallyUnscopedEntityTypes)}. If this entity is genuinely tenant-scoped, " +
                    "implement the appropriate marker interface. If it is deliberately platform-global " +
                    $"(like User), add it to {nameof(IntentionallyUnscopedEntityTypes)} explicitly.");
            }
        }
    }

    private void SetCompanySelfFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, IDeactivatable, IEntityBase
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(e =>
            _tenantContext.IsSuperAdmin ||
            (_tenantContext.CompanyId != null && e.Id == _tenantContext.CompanyId && e.IsActive));
    }

    private void SetCompanyScopedFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ICompanyScoped
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(e =>
            _tenantContext.IsSuperAdmin || (_tenantContext.CompanyId != null && e.CompanyId == _tenantContext.CompanyId));
    }

    private void SetCompanyScopedDeactivatableFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ICompanyScoped, IDeactivatable
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(e =>
            _tenantContext.IsSuperAdmin ||
            (_tenantContext.CompanyId != null && e.CompanyId == _tenantContext.CompanyId && e.IsActive));
    }

    private void SetNullableTenantFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ITenantScoped
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(e =>
            _tenantContext.IsSuperAdmin ||
            ((e.CompanyId == null || e.CompanyId == _tenantContext.CompanyId) &&
             (e.BranchId == null || _tenantContext.BranchId == null || e.BranchId == _tenantContext.BranchId)));
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<EntityBase>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<ICompanyScoped>())
        {
            if (entry.State == EntityState.Added && entry.Entity.CompanyId == 0 && _tenantContext.CompanyId.HasValue)
            {
                entry.Entity.CompanyId = _tenantContext.CompanyId.Value;
            }
        }

        return await base.SaveChangesAsync(cancellationToken);
    }
}
