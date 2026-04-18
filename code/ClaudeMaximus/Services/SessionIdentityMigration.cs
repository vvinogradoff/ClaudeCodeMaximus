using System.Collections.Generic;
using ClaudeMaximus.Models;
using Serilog;

namespace ClaudeMaximus.Services;

/// <summary>
/// One-time migration that populates ExternalId from ClaudeSessionId for existing sessions.
/// ClaudeSessionId is the same UUID used by the Tessyn daemon as external_id, so this
/// is a simple copy rather than a lookup.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionIdentityMigration
{
    private static readonly ILogger _log = Log.ForContext<SessionIdentityMigration>();

    private readonly IAppSettingsService _appSettings;

    public SessionIdentityMigration(IAppSettingsService appSettings)
    {
        _appSettings = appSettings;
    }

    /// <summary>
    /// Walks the entire session tree and sets ExternalId = ClaudeSessionId for any node
    /// that has a ClaudeSessionId but no ExternalId. Also migrates ActiveSessionFileName
    /// to ActiveSessionExternalId if possible. Returns the number of nodes migrated.
    /// </summary>
    public int MigrateAll()
    {
        var settings = _appSettings.Settings;
        var migrated = 0;
        var needsSave = false;

        foreach (var dir in settings.Tree)
        {
            migrated += MigrateChildren(dir.Sessions);
            migrated += MigrateGroups(dir.Groups);
        }

        if (migrated > 0)
            needsSave = true;

        // Migrate active session reference
        if (string.IsNullOrEmpty(settings.ActiveSessionExternalId) &&
            !string.IsNullOrEmpty(settings.ActiveSessionFileName))
        {
            var externalId = FindExternalIdByFileName(settings.Tree, settings.ActiveSessionFileName);
            if (externalId != null)
            {
                settings.ActiveSessionExternalId = externalId;
                needsSave = true;
                _log.Information("Migrated ActiveSession from FileName={FileName} to ExternalId={ExternalId}",
                    settings.ActiveSessionFileName, externalId);
            }
        }

        if (needsSave)
        {
            _appSettings.Save();
            _log.Information("Identity migration: populated ExternalId for {Count} session(s)", migrated);
        }

        return migrated;
    }

    private static int MigrateGroups(List<GroupNodeModel>? groups)
    {
        if (groups == null) return 0;
        var migrated = 0;
        foreach (var group in groups)
        {
            migrated += MigrateChildren(group.Sessions);
            migrated += MigrateGroups(group.Groups);
        }
        return migrated;
    }

    private static int MigrateChildren(List<SessionNodeModel>? sessions)
    {
        if (sessions == null) return 0;
        var migrated = 0;
        foreach (var session in sessions)
        {
            if (session.ExternalId == null && session.ClaudeSessionId != null)
            {
                session.ExternalId = session.ClaudeSessionId;
                migrated++;
            }
        }
        return migrated;
    }

    private static string? FindExternalIdByFileName(List<DirectoryNodeModel> tree, string fileName)
    {
        foreach (var dir in tree)
        {
            var found = FindInSessions(dir.Sessions, fileName) ?? FindInGroups(dir.Groups, fileName);
            if (found != null) return found;
        }
        return null;
    }

    private static string? FindInGroups(List<GroupNodeModel>? groups, string fileName)
    {
        if (groups == null) return null;
        foreach (var group in groups)
        {
            var found = FindInSessions(group.Sessions, fileName) ?? FindInGroups(group.Groups, fileName);
            if (found != null) return found;
        }
        return null;
    }

    private static string? FindInSessions(List<SessionNodeModel>? sessions, string fileName)
    {
        if (sessions == null) return null;
        foreach (var session in sessions)
        {
            if (session.FileName == fileName)
                return session.ExternalId;
        }
        return null;
    }
}
