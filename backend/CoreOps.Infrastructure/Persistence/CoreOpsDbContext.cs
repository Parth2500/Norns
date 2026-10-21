using System.Reflection;
using CoreOps.Application.Common.Security;
using CoreOps.Domain.Audit;
using CoreOps.Domain.Common;
using CoreOps.Domain.Identity;
using CoreOps.Domain.Operations;
using CoreOps.Domain.Workflow;
using Microsoft.EntityFrameworkCore;

namespace CoreOps.Infrastructure.Persistence;

public class CoreOpsDbContext : DbContext
{
    private static readonly MethodInfo ApplyTenantFilterMethod =
        typeof(CoreOpsDbContext).GetMethod(nameof(ApplyTenantFilter), BindingFlags.Instance | BindingFlags.NonPublic)!;

    private readonly ICurrentUser _currentUser;

    public CoreOpsDbContext(DbContextOptions<CoreOpsDbContext> options, ICurrentUser currentUser)
        : base(options)
    {
        _currentUser = currentUser;
    }

    // EF Core reads this again for every query, so each request sees only its own tenant's rows.
    private Guid CurrentTenantId => _currentUser.TenantId;

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();

    public DbSet<WorkflowDefinition> WorkflowDefinitions => Set<WorkflowDefinition>();
    public DbSet<WorkflowState> WorkflowStates => Set<WorkflowState>();
    public DbSet<WorkflowTransition> WorkflowTransitions => Set<WorkflowTransition>();

    public DbSet<Request> Requests => Set<Request>();
    public DbSet<RequestStateHistory> RequestStateHistories => Set<RequestStateHistory>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CoreOpsDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(t => typeof(Entity).IsAssignableFrom(t.ClrType)))
        {
            ApplyTenantFilterMethod.MakeGenericMethod(entityType.ClrType).Invoke(this, [modelBuilder]);
        }
    }

    private void ApplyTenantFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : Entity
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
    }
}
