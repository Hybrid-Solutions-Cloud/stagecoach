using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Stagecoach.Core;

namespace Stagecoach.Infrastructure.Storage;

public sealed class EncryptedSqliteMetadataStore : IMetadataStore
{
    private static readonly byte[] KeyEntropy = Encoding.UTF8.GetBytes("HCS.Stagecoach.Metadata.v1");
    private readonly string _databasePath;
    private readonly string _keyPath;
    private string? _keyHex;

    public EncryptedSqliteMetadataStore(string? databasePath = null, string? keyPath = null)
    {
        _databasePath = databasePath ?? StagecoachPaths.DatabasePath;
        _keyPath = keyPath ?? StagecoachPaths.DatabaseKeyPath;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        SQLitePCL.Batteries_V2.Init();
        var directory = Path.GetDirectoryName(_databasePath) ?? ".";
        Directory.CreateDirectory(directory);

        // Fail here, naming the path, rather than letting SQLite surface "attempt to write a
        // readonly database" from somewhere deep in a later command.
        StagecoachPaths.AssertWritable(directory);

        _keyHex = LoadOrCreateKey();
        await using var connection = await OpenAsync(cancellationToken);
        await ApplyJournalModeAsync(connection, cancellationToken);
        var sql = """
            PRAGMA foreign_keys=ON;
            CREATE TABLE IF NOT EXISTS Identities (
                Id TEXT PRIMARY KEY,
                DisplayName TEXT NOT NULL,
                AccountName TEXT NOT NULL,
                AzureConfigDirectory TEXT NOT NULL,
                AuthenticationState INTEGER NOT NULL,
                LastAuthenticatedAt TEXT NULL,
                IsEnabled INTEGER NOT NULL,
                LastErrorCategory TEXT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_Identities_ConfigDirectory ON Identities(AzureConfigDirectory);
            CREATE TABLE IF NOT EXISTS Tenants (
                IdentityId TEXT NOT NULL,
                TenantId TEXT NOT NULL,
                DisplayName TEXT NOT NULL,
                IsEnabled INTEGER NOT NULL,
                RequiresReview INTEGER NOT NULL,
                PRIMARY KEY (IdentityId, TenantId),
                FOREIGN KEY (IdentityId) REFERENCES Identities(Id) ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS Subscriptions (
                IdentityId TEXT NOT NULL,
                TenantId TEXT NOT NULL,
                SubscriptionId TEXT NOT NULL,
                DisplayName TEXT NOT NULL,
                State TEXT NOT NULL,
                IsEnabled INTEGER NOT NULL,
                RequiresReview INTEGER NOT NULL,
                PRIMARY KEY (IdentityId, SubscriptionId),
                FOREIGN KEY (IdentityId, TenantId) REFERENCES Tenants(IdentityId, TenantId) ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS Machines (
                ResourceId TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Kind INTEGER NOT NULL,
                OperatingSystem INTEGER NOT NULL,
                OperatingSystemName TEXT NOT NULL,
                ResourceGroup TEXT NOT NULL,
                Location TEXT NOT NULL,
                PowerState TEXT NOT NULL,
                AgentState TEXT NOT NULL,
                PrivateIpAddress TEXT NULL,
                PublicIpAddress TEXT NULL,
                VirtualNetworkId TEXT NULL,
                DomainName TEXT NULL,
                TagsJson TEXT NOT NULL,
                LastDiscoveredAt TEXT NOT NULL,
                SupportsEntraLogin INTEGER NOT NULL DEFAULT 0,
                IsFavorite INTEGER NOT NULL DEFAULT 0,
                LastConnectedAt TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_Machines_Name ON Machines(Name);
            CREATE INDEX IF NOT EXISTS IX_Machines_Domain ON Machines(DomainName);
            CREATE TABLE IF NOT EXISTS AccessPaths (
                MachineResourceId TEXT NOT NULL,
                IdentityId TEXT NOT NULL,
                TenantId TEXT NOT NULL,
                SubscriptionId TEXT NOT NULL,
                Route INTEGER NOT NULL,
                Readiness INTEGER NOT NULL,
                Reason TEXT NOT NULL,
                BastionResourceId TEXT NULL,
                IsPreferred INTEGER NOT NULL,
                LastSeenAt TEXT NOT NULL,
                PRIMARY KEY (MachineResourceId, IdentityId, Route),
                FOREIGN KEY (MachineResourceId) REFERENCES Machines(ResourceId) ON DELETE CASCADE,
                FOREIGN KEY (IdentityId) REFERENCES Identities(Id) ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS ConnectionIdentities (
                Id TEXT PRIMARY KEY,
                DisplayName TEXT NOT NULL,
                Kind INTEGER NOT NULL,
                Username TEXT NOT NULL,
                CredentialTarget TEXT NULL,
                SshPrivateKeyPath TEXT NULL,
                IsEnabled INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS ConnectionMappings (
                Id TEXT PRIMARY KEY,
                ConnectionIdentityId TEXT NOT NULL,
                ScopeKind INTEGER NOT NULL,
                MatchValue TEXT NOT NULL,
                Priority INTEGER NOT NULL,
                IsRelayIdentity INTEGER NOT NULL,
                FOREIGN KEY (ConnectionIdentityId) REFERENCES ConnectionIdentities(Id) ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS AuditEvents (
                Id TEXT PRIMARY KEY,
                OccurredAt TEXT NOT NULL,
                Category INTEGER NOT NULL,
                Summary TEXT NOT NULL,
                Detail TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_AuditEvents_OccurredAt ON AuditEvents(OccurredAt DESC);
            CREATE TABLE IF NOT EXISTS MachinePins (
                ResourceId TEXT PRIMARY KEY,
                ConnectionIdentityId TEXT NOT NULL,
                FOREIGN KEY (ConnectionIdentityId) REFERENCES ConnectionIdentities(Id) ON DELETE CASCADE
            );
            """;
        await ExecuteAsync(connection, sql, cancellationToken);

        // Databases created before this column existed are upgraded in place; CREATE TABLE IF NOT
        // EXISTS leaves an older table untouched.
        await AddColumnIfMissingAsync(connection, "Machines", "SupportsEntraLogin", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
    }

    private static async Task AddColumnIfMissingAsync(
        SqliteConnection connection, string table, string column, string definition, CancellationToken cancellationToken)
    {
        await using var check = connection.CreateCommand();
        check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{column}'";
        if (Convert.ToInt32(await check.ExecuteScalarAsync(cancellationToken)) > 0) return;
        await ExecuteAsync(connection, $"ALTER TABLE {table} ADD COLUMN {column} {definition};", cancellationToken);
    }

    public async Task<IReadOnlyList<AzureIdentityProfile>> GetIdentitiesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM Identities ORDER BY DisplayName";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var results = new List<AzureIdentityProfile>();
        while (await reader.ReadAsync(cancellationToken)) results.Add(ReadIdentity(reader));
        return results;
    }

    public async Task UpsertIdentityAsync(AzureIdentityProfile identity, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Identities VALUES ($id,$display,$account,$directory,$state,$last,$enabled,$error)
            ON CONFLICT(Id) DO UPDATE SET DisplayName=$display,AccountName=$account,
                AzureConfigDirectory=$directory,AuthenticationState=$state,LastAuthenticatedAt=$last,
                IsEnabled=$enabled,LastErrorCategory=$error
            """;
        Add(command, "$id", identity.Id.ToString("D"));
        Add(command, "$display", identity.DisplayName);
        Add(command, "$account", identity.AccountName);
        Add(command, "$directory", identity.AzureConfigDirectory);
        Add(command, "$state", (int)identity.AuthenticationState);
        Add(command, "$last", Format(identity.LastAuthenticatedAt));
        Add(command, "$enabled", identity.IsEnabled ? 1 : 0);
        Add(command, "$error", identity.LastErrorCategory);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RemoveIdentityAsync(Guid identityId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Identities WHERE Id=$id";
        Add(command, "$id", identityId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task<IReadOnlyList<TenantScope>> GetTenantsAsync(Guid identityId, CancellationToken cancellationToken = default) =>
        ReadListAsync(identityId, "SELECT * FROM Tenants WHERE IdentityId=$id ORDER BY DisplayName",
            reader => new TenantScope(identityId, reader.GetString(1), reader.GetString(2), reader.GetInt32(3) != 0, reader.GetInt32(4) != 0), cancellationToken);

    public Task<IReadOnlyList<SubscriptionScope>> GetSubscriptionsAsync(Guid identityId, CancellationToken cancellationToken = default) =>
        ReadListAsync(identityId, "SELECT * FROM Subscriptions WHERE IdentityId=$id ORDER BY DisplayName",
            reader => new SubscriptionScope(identityId, reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetInt32(5) != 0, reader.GetInt32(6) != 0), cancellationToken);

    public async Task UpsertIdentityInventoryAsync(IdentityInventory inventory, CancellationToken cancellationToken = default)
    {
        await UpsertIdentityAsync(inventory.Identity, cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        foreach (var tenant in inventory.Tenants)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO Tenants VALUES ($identity,$tenant,$display,$enabled,$review)
                ON CONFLICT(IdentityId,TenantId) DO UPDATE SET DisplayName=$display,
                    IsEnabled=$enabled,RequiresReview=$review
                """;
            Add(command, "$identity", tenant.IdentityId.ToString("D"));
            Add(command, "$tenant", tenant.TenantId);
            Add(command, "$display", tenant.DisplayName);
            Add(command, "$enabled", tenant.IsEnabled ? 1 : 0);
            Add(command, "$review", tenant.RequiresReview ? 1 : 0);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (var subscription in inventory.Subscriptions)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO Subscriptions VALUES ($identity,$tenant,$subscription,$display,$state,$enabled,$review)
                ON CONFLICT(IdentityId,SubscriptionId) DO UPDATE SET TenantId=$tenant,DisplayName=$display,
                    State=$state,IsEnabled=$enabled,RequiresReview=$review
                """;
            Add(command, "$identity", subscription.IdentityId.ToString("D"));
            Add(command, "$tenant", subscription.TenantId);
            Add(command, "$subscription", subscription.SubscriptionId);
            Add(command, "$display", subscription.DisplayName);
            Add(command, "$state", subscription.State);
            Add(command, "$enabled", subscription.IsEnabled ? 1 : 0);
            Add(command, "$review", subscription.RequiresReview ? 1 : 0);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public Task SetTenantEnabledAsync(Guid identityId, string tenantId, bool enabled, CancellationToken cancellationToken = default) =>
        SetEnabledAsync("Tenants", "TenantId", identityId, tenantId, enabled, cancellationToken);

    public Task SetSubscriptionEnabledAsync(Guid identityId, string subscriptionId, bool enabled, CancellationToken cancellationToken = default) =>
        SetEnabledAsync("Subscriptions", "SubscriptionId", identityId, subscriptionId, enabled, cancellationToken);

    public async Task<IReadOnlyList<MachineRecord>> GetMachinesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var paths = new Dictionary<string, List<AzureAccessPath>>(StringComparer.OrdinalIgnoreCase);
        await using (var pathCommand = connection.CreateCommand())
        {
            pathCommand.CommandText = "SELECT * FROM AccessPaths";
            await using var reader = await pathCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var resourceId = reader.GetString(0);
                if (!paths.TryGetValue(resourceId, out var list)) paths[resourceId] = list = [];
                list.Add(new AzureAccessPath(
                    Guid.Parse(reader.GetString(1)), reader.GetString(2), reader.GetString(3),
                    (ConnectionRouteKind)reader.GetInt32(4), (ReadinessState)reader.GetInt32(5),
                    reader.GetString(6), NullableString(reader, 7), reader.GetInt32(8) != 0));
            }
        }
        await using var command = connection.CreateCommand();
        // Columns are named, never "SELECT *". ALTER TABLE ADD COLUMN appends to the end, so a
        // database created before a column was introduced has a different physical order than a
        // fresh one, and positional reads silently line up against the wrong fields.
        command.CommandText = """
            SELECT ResourceId,Name,Kind,OperatingSystem,OperatingSystemName,ResourceGroup,Location,
                   PowerState,AgentState,PrivateIpAddress,PublicIpAddress,VirtualNetworkId,DomainName,
                   TagsJson,LastDiscoveredAt,SupportsEntraLogin,IsFavorite,LastConnectedAt
            FROM Machines ORDER BY IsFavorite DESC,Name
            """;
        await using var machineReader = await command.ExecuteReaderAsync(cancellationToken);
        var machines = new List<MachineRecord>();
        while (await machineReader.ReadAsync(cancellationToken))
        {
            var resourceId = machineReader.GetString(0);
            var tags = JsonSerializer.Deserialize<Dictionary<string, string>>(machineReader.GetString(13)) ?? [];
            machines.Add(new MachineRecord(
                resourceId, machineReader.GetString(1), (MachineKind)machineReader.GetInt32(2),
                (OperatingSystemKind)machineReader.GetInt32(3), machineReader.GetString(4),
                machineReader.GetString(5), machineReader.GetString(6), machineReader.GetString(7),
                machineReader.GetString(8), NullableString(machineReader, 9), NullableString(machineReader, 10),
                NullableString(machineReader, 11), NullableString(machineReader, 12), tags,
                paths.GetValueOrDefault(resourceId) ?? [], DateTimeOffset.Parse(machineReader.GetString(14)),
                Flag(machineReader, 15), Flag(machineReader, 16), ParseDate(machineReader, 17)));
        }
        return machines;
    }

    public async Task UpsertDiscoveryAsync(DiscoveryResult result, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = "DELETE FROM AccessPaths WHERE IdentityId=$identity";
            Add(clear, "$identity", result.IdentityId.ToString("D"));
            await clear.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (var machine in result.Machines)
        {
            await UpsertMachineAsync(connection, transaction, machine, cancellationToken);
            foreach (var path in machine.AccessPaths.Where(item => item.IdentityId == result.IdentityId))
                await UpsertPathAsync(connection, transaction, machine.ResourceId, path, result.CompletedAt, cancellationToken);
        }
        await using (var prune = connection.CreateCommand())
        {
            prune.Transaction = transaction;
            prune.CommandText = "DELETE FROM Machines WHERE NOT EXISTS (SELECT 1 FROM AccessPaths WHERE MachineResourceId=Machines.ResourceId)";
            await prune.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public Task SetFavoriteAsync(string resourceId, bool favorite, CancellationToken cancellationToken = default) =>
        UpdateMachineAsync(resourceId, "IsFavorite", favorite ? 1 : 0, cancellationToken);

    public Task RecordConnectionAsync(string resourceId, CancellationToken cancellationToken = default) =>
        UpdateMachineAsync(resourceId, "LastConnectedAt", DateTimeOffset.UtcNow.ToString("O"), cancellationToken);

    public async Task<IReadOnlyList<ConnectionIdentityProfile>> GetConnectionIdentitiesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM ConnectionIdentities ORDER BY DisplayName";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var results = new List<ConnectionIdentityProfile>();
        while (await reader.ReadAsync(cancellationToken))
            results.Add(new ConnectionIdentityProfile(Guid.Parse(reader.GetString(0)), reader.GetString(1),
                (ConnectionIdentityKind)reader.GetInt32(2), reader.GetString(3), NullableString(reader, 4),
                NullableString(reader, 5), reader.GetInt32(6) != 0));
        return results;
    }

    public async Task UpsertConnectionIdentityAsync(ConnectionIdentityProfile profile, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ConnectionIdentities VALUES ($id,$display,$kind,$username,$credential,$key,$enabled)
            ON CONFLICT(Id) DO UPDATE SET DisplayName=$display,Kind=$kind,Username=$username,
                CredentialTarget=$credential,SshPrivateKeyPath=$key,IsEnabled=$enabled
            """;
        Add(command, "$id", profile.Id.ToString("D"));
        Add(command, "$display", profile.DisplayName);
        Add(command, "$kind", (int)profile.Kind);
        Add(command, "$username", profile.Username);
        Add(command, "$credential", profile.CredentialTarget);
        Add(command, "$key", profile.SshPrivateKeyPath);
        Add(command, "$enabled", profile.IsEnabled ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RemoveConnectionIdentityAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ConnectionIdentities WHERE Id=$id";
        Add(command, "$id", profileId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ConnectionIdentityMapping>> GetConnectionMappingsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM ConnectionMappings ORDER BY Priority DESC";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var results = new List<ConnectionIdentityMapping>();
        while (await reader.ReadAsync(cancellationToken))
            results.Add(new ConnectionIdentityMapping(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)),
                (MappingScopeKind)reader.GetInt32(2), reader.GetString(3), reader.GetInt32(4), reader.GetInt32(5) != 0));
        return results;
    }

    public async Task UpsertConnectionMappingAsync(ConnectionIdentityMapping mapping, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ConnectionMappings VALUES ($id,$identity,$scope,$match,$priority,$relay)
            ON CONFLICT(Id) DO UPDATE SET ConnectionIdentityId=$identity,ScopeKind=$scope,
                MatchValue=$match,Priority=$priority,IsRelayIdentity=$relay
            """;
        Add(command, "$id", mapping.Id.ToString("D"));
        Add(command, "$identity", mapping.ConnectionIdentityId.ToString("D"));
        Add(command, "$scope", (int)mapping.ScopeKind);
        Add(command, "$match", mapping.MatchValue);
        Add(command, "$priority", mapping.Priority);
        Add(command, "$relay", mapping.IsRelayIdentity ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RemoveConnectionMappingAsync(Guid mappingId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ConnectionMappings WHERE Id=$id";
        Add(command, "$id", mappingId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AppendAuditAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO AuditEvents (Id, OccurredAt, Category, Summary, Detail)
            VALUES ($id,$at,$category,$summary,$detail);
            DELETE FROM AuditEvents WHERE Id NOT IN (
                SELECT Id FROM AuditEvents ORDER BY OccurredAt DESC LIMIT 2000);
            """;
        Add(command, "$id", auditEvent.Id.ToString("D"));
        Add(command, "$at", auditEvent.OccurredAt.ToString("O"));
        Add(command, "$category", (int)auditEvent.Category);
        Add(command, "$summary", auditEvent.Summary);
        Add(command, "$detail", auditEvent.Detail);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuditEvent>> GetRecentAuditAsync(
        int maximumEvents = 200, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT Id, OccurredAt, Category, Summary, Detail FROM AuditEvents ORDER BY OccurredAt DESC LIMIT $limit";
        Add(command, "$limit", Math.Clamp(maximumEvents, 1, 2000));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var results = new List<AuditEvent>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new AuditEvent(
                Guid.Parse(reader.GetString(0)),
                DateTimeOffset.Parse(reader.GetString(1), System.Globalization.CultureInfo.InvariantCulture),
                (AuditCategory)reader.GetInt32(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return results;
    }

    public async Task<IReadOnlyDictionary<string, Guid>> GetMachinePinsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ResourceId, ConnectionIdentityId FROM MachinePins";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var results = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(cancellationToken))
            results[reader.GetString(0)] = Guid.Parse(reader.GetString(1));
        return results;
    }

    public async Task SetMachinePinAsync(string resourceId, Guid? connectionIdentityId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        if (connectionIdentityId is { } identityId)
        {
            command.CommandText = """
                INSERT INTO MachinePins (ResourceId, ConnectionIdentityId) VALUES ($resource,$identity)
                ON CONFLICT(ResourceId) DO UPDATE SET ConnectionIdentityId=$identity
                """;
            Add(command, "$identity", identityId.ToString("D"));
        }
        else
        {
            command.CommandText = "DELETE FROM MachinePins WHERE ResourceId=$resource";
        }

        Add(command, "$resource", resourceId.ToUpperInvariant());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Write-ahead logging needs to create <c>-wal</c> and <c>-shm</c> files beside the database.
    /// That fails on redirected AppData (OneDrive, folder redirection to a network share) and under
    /// Controlled Folder Access, and SQLite reports it as "attempt to write a readonly database".
    /// Rolled-back journalling is slower but works everywhere, so fall back rather than refuse to start.
    /// </summary>
    private static async Task ApplyJournalModeAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            await ExecuteAsync(connection, "PRAGMA journal_mode=WAL;", cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode;";
            var mode = (await command.ExecuteScalarAsync(cancellationToken))?.ToString();
            if (string.Equals(mode, "wal", StringComparison.OrdinalIgnoreCase)) return;
        }
        catch (SqliteException)
        {
            // Fall through to the portable journal mode below.
        }

        await ExecuteAsync(connection, "PRAGMA journal_mode=DELETE;", cancellationToken);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        _keyHex ??= LoadOrCreateKey();
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        await connection.OpenAsync(cancellationToken);
        await ExecuteAsync(connection, $"PRAGMA key = \"x'{_keyHex}'\"; PRAGMA foreign_keys=ON;", cancellationToken);
        return connection;
    }

    /// <summary>
    /// Extra entropy mixed into the DPAPI protection of the metadata key. Empty in normal operation:
    /// Stagecoach has no application passphrase, and the key is protected by Windows for the owning
    /// account alone. It carries a value only while removing the passphrase an older version set, so
    /// that key can be unwrapped once and rewrapped without it.
    /// </summary>
    private byte[] _additionalEntropy = [];

    public void UseAdditionalEntropy(byte[]? entropy)
    {
        _additionalEntropy = entropy is { Length: > 0 } ? [.. entropy] : [];
        _keyHex = null;
    }

    /// <summary>Re-wraps the existing key under new entropy. Used to remove an older version's passphrase.</summary>
    public void RewrapKey(byte[]? newEntropy)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Stagecoach metadata protection requires Windows.");
        var key = Convert.FromHexString(_keyHex ??= LoadOrCreateKey());
        var replacement = newEntropy is { Length: > 0 } ? newEntropy : [];
        var reprotected = ProtectedData.Protect(key, Entropy(replacement), DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_keyPath, reprotected);
        _additionalEntropy = [.. replacement];
    }

    private byte[] Entropy(byte[] additional)
    {
        if (additional.Length == 0) return KeyEntropy;
        var combined = new byte[KeyEntropy.Length + additional.Length];
        KeyEntropy.CopyTo(combined, 0);
        additional.CopyTo(combined, KeyEntropy.Length);
        return combined;
    }

    private string LoadOrCreateKey()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Stagecoach metadata protection requires Windows.");
        var entropy = Entropy(_additionalEntropy);
        if (File.Exists(_keyPath))
        {
            var protectedKey = File.ReadAllBytes(_keyPath);
            byte[] key;
            try
            {
                key = ProtectedData.Unprotect(protectedKey, entropy, DataProtectionScope.CurrentUser);
            }
            catch (CryptographicException exception)
            {
                throw new CryptographicException(
                    "The Stagecoach metadata key could not be unwrapped. It is protected for a " +
                    "different Windows account, or was wrapped with a passphrase not supplied here.", exception);
            }

            if (key.Length != 32) throw new CryptographicException("The Stagecoach metadata key is invalid.");
            return Convert.ToHexString(key);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_keyPath) ?? ".");
        var generated = RandomNumberGenerator.GetBytes(32);
        var protectedGenerated = ProtectedData.Protect(generated, entropy, DataProtectionScope.CurrentUser);
        using (var stream = new FileStream(_keyPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            stream.Write(protectedGenerated);
        return Convert.ToHexString(generated);
    }

    private async Task<IReadOnlyList<T>> ReadListAsync<T>(Guid id, string sql, Func<SqliteDataReader, T> read, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        Add(command, "$id", id.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var results = new List<T>();
        while (await reader.ReadAsync(cancellationToken)) results.Add(read(reader));
        return results;
    }

    private async Task SetEnabledAsync(string table, string keyColumn, Guid identityId, string key, bool enabled, CancellationToken cancellationToken)
    {
        if (table is not ("Tenants" or "Subscriptions") || keyColumn is not ("TenantId" or "SubscriptionId"))
            throw new ArgumentOutOfRangeException(nameof(table));
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE {table} SET IsEnabled=$enabled,RequiresReview=0 WHERE IdentityId=$identity AND {keyColumn}=$key";
        Add(command, "$enabled", enabled ? 1 : 0);
        Add(command, "$identity", identityId.ToString("D"));
        Add(command, "$key", key);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task UpdateMachineAsync(string resourceId, string column, object value, CancellationToken cancellationToken)
    {
        if (column is not ("IsFavorite" or "LastConnectedAt")) throw new ArgumentOutOfRangeException(nameof(column));
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE Machines SET {column}=$value WHERE ResourceId=$id";
        Add(command, "$value", value);
        Add(command, "$id", resourceId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertMachineAsync(SqliteConnection connection, SqliteTransaction transaction, MachineRecord machine, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Machines (ResourceId,Name,Kind,OperatingSystem,OperatingSystemName,ResourceGroup,
                Location,PowerState,AgentState,PrivateIpAddress,PublicIpAddress,VirtualNetworkId,DomainName,
                TagsJson,LastDiscoveredAt,SupportsEntraLogin,IsFavorite,LastConnectedAt)
            VALUES ($id,$name,$kind,$os,$osName,$rg,$location,$power,$agent,$private,$public,$vnet,$domain,$tags,$seen,$entra,$favorite,$connected)
            ON CONFLICT(ResourceId) DO UPDATE SET Name=$name,Kind=$kind,OperatingSystem=$os,
                OperatingSystemName=$osName,ResourceGroup=$rg,Location=$location,PowerState=$power,
                AgentState=$agent,PrivateIpAddress=$private,PublicIpAddress=$public,VirtualNetworkId=$vnet,
                DomainName=$domain,TagsJson=$tags,LastDiscoveredAt=$seen,SupportsEntraLogin=$entra
            """;
        Add(command, "$id", machine.ResourceId);
        Add(command, "$name", machine.Name);
        Add(command, "$kind", (int)machine.Kind);
        Add(command, "$os", (int)machine.OperatingSystem);
        Add(command, "$osName", machine.OperatingSystemName);
        Add(command, "$rg", machine.ResourceGroup);
        Add(command, "$location", machine.Location);
        Add(command, "$power", machine.PowerState);
        Add(command, "$agent", machine.AgentState);
        Add(command, "$private", machine.PrivateIpAddress);
        Add(command, "$public", machine.PublicIpAddress);
        Add(command, "$vnet", machine.VirtualNetworkId);
        Add(command, "$domain", machine.DomainName);
        Add(command, "$tags", JsonSerializer.Serialize(machine.Tags));
        Add(command, "$seen", machine.LastDiscoveredAt.ToString("O"));
        Add(command, "$entra", machine.SupportsEntraLogin ? 1 : 0);
        Add(command, "$favorite", machine.IsFavorite ? 1 : 0);
        Add(command, "$connected", Format(machine.LastConnectedAt));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertPathAsync(SqliteConnection connection, SqliteTransaction transaction, string resourceId, AzureAccessPath path, DateTimeOffset seenAt, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO AccessPaths VALUES ($machine,$identity,$tenant,$subscription,$route,$readiness,$reason,$bastion,$preferred,$seen)
            ON CONFLICT(MachineResourceId,IdentityId,Route) DO UPDATE SET TenantId=$tenant,
                SubscriptionId=$subscription,Readiness=$readiness,Reason=$reason,BastionResourceId=$bastion,
                IsPreferred=$preferred,LastSeenAt=$seen
            """;
        Add(command, "$machine", resourceId);
        Add(command, "$identity", path.IdentityId.ToString("D"));
        Add(command, "$tenant", path.TenantId);
        Add(command, "$subscription", path.SubscriptionId);
        Add(command, "$route", (int)path.Route);
        Add(command, "$readiness", (int)path.Readiness);
        Add(command, "$reason", path.Reason);
        Add(command, "$bastion", path.BastionResourceId);
        Add(command, "$preferred", path.IsPreferred ? 1 : 0);
        Add(command, "$seen", seenAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static AzureIdentityProfile ReadIdentity(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3),
        (AuthenticationState)reader.GetInt32(4), ParseDate(reader, 5), reader.GetInt32(6) != 0, NullableString(reader, 7));

    private static void Add(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static string? NullableString(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    /// <summary>
    /// A boolean column that tolerates NULL. Rows written before a column existed, or written by a
    /// version that put values in the wrong place, can hold NULL where a flag is expected; that must
    /// read as "not set" rather than throwing and taking the whole machine list with it.
    /// </summary>
    private static bool Flag(SqliteDataReader reader, int ordinal) =>
        !reader.IsDBNull(ordinal) && reader.GetInt32(ordinal) != 0;

    private static DateTimeOffset? ParseDate(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : DateTimeOffset.Parse(reader.GetString(ordinal));
    private static string? Format(DateTimeOffset? value) => value?.ToString("O");
}
