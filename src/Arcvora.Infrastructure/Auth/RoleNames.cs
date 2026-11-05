using Arcvora.Infrastructure.Persistence;

namespace Arcvora.Infrastructure.Auth;

/// <summary>API'deki rol adları (snake_case): admin | editor | reader | app_user.</summary>
public static class RoleNames
{
    public static string ToApi(WorkspaceRole role) => role switch
    {
        WorkspaceRole.Admin => "admin",
        WorkspaceRole.Editor => "editor",
        WorkspaceRole.AppUser => "app_user",
        _ => "reader",
    };

    public static bool TryParse(string? text, out WorkspaceRole role)
    {
        switch ((text ?? "").Trim().ToLowerInvariant())
        {
            case "admin": role = WorkspaceRole.Admin; return true;
            case "editor": role = WorkspaceRole.Editor; return true;
            case "reader": role = WorkspaceRole.Reader; return true;
            case "app_user" or "appuser": role = WorkspaceRole.AppUser; return true;
            default: role = WorkspaceRole.Reader; return false;
        }
    }
}
