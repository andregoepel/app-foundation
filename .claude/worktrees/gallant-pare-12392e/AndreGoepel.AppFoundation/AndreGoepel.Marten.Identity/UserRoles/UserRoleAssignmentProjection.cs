using AndreGoepel.Marten.Identity.Users.Events;
using JasperFx.Events;
using Marten;
using Marten.Events.Projections;

namespace AndreGoepel.Marten.Identity.UserRoles;

internal class UserRoleAssignmentProjection : IProjection
{
    public static void Apply(IDocumentOperations operations, RoleAssigned @event)
    {
        var assignment = new UserRoleAssignment { UserId = @event.UserId, RoleId = @event.RoleId };
        operations.Store(assignment);
    }

    public static void Apply(IDocumentOperations operations, RoleUnassigned @event)
    {
        operations.DeleteWhere<UserRoleAssignment>(userRoleAssignment =>
            userRoleAssignment.UserGuid == @event.UserId
            && userRoleAssignment.RoleGuid == @event.RoleId
        );
    }

    public Task ApplyAsync(
        IDocumentOperations operations,
        IReadOnlyList<IEvent> events,
        CancellationToken cancellation
    )
    {
        foreach (var @event in events)
        {
            switch (@event.Data)
            {
                case RoleAssigned assigned:
                    Apply(operations, assigned);
                    break;
                case RoleUnassigned unassigned:
                    Apply(operations, unassigned);
                    break;
            }
        }

        return Task.CompletedTask;
    }
}
