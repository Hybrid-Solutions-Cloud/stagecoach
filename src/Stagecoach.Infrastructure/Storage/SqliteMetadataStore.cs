using System.Text.Json;
using Microsoft.Data.Sqlite;
using Stagecoach.Core.Interfaces;
using Stagecoach.Core.Models;

namespace Stagecoach.Infrastructure.Storage;

public class SqliteMetadataStore : IMetadataStore
{
    private readonly string _connectionString;

    public SqliteMetadataStore(string? dbPath = null)
    {
        if (string.IsNullOrWhiteSpace(dbPath))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var folder = Path.Combine(appData, ".stagecoach");
            Directory.CreateDirectory(folder);
            dbPath = Path.Combine(folder, "stagecoach.db");
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = @"
            CREATE TABLE IF NOT EXISTS Machines (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                ResourceGroup TEXT NOT NULL,
                SubscriptionId TEXT NOT NULL,
                TenantId TEXT NOT NULL,
                Location TEXT NOT NULL,
                Kind INTEGER NOT NULL,
                OsType TEXT NOT NULL,
                OsName TEXT NOT NULL,
                PowerState TEXT NOT NULL,
                AgentStatus TEXT,
                DomainName TEXT,
                DomainType INTEGER NOT NULL,
                BastionHostId TEXT,
                PublicIpAddress TEXT,
                PrivateIpAddress TEXT,
                IsFavorite INTEGER NOT NULL DEFAULT 0,
                LastConnectedAt TEXT,
                TagsJson TEXT
            );
            CREATE INDEX IF NOT EXISTS IX_Machines_Name ON Machines(Name);
            CREATE INDEX IF NOT EXISTS IX_Machines_DomainName ON Machines(DomainName);
        ";

        using var command = new SqliteCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StagecoachMachine>> GetAllMachinesAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<StagecoachMachine>();
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = "SELECT * FROM Machines ORDER BY IsFavorite DESC, Name ASC";
        using var command = new SqliteCommand(sql, connection);
        using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(ReadMachine(reader));
        }

        return list;
    }

    public async Task SaveMachinesAsync(IEnumerable<StagecoachMachine> machines, CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        var sql = @"
            INSERT INTO Machines (Id, Name, ResourceGroup, SubscriptionId, TenantId, Location, Kind, OsType, OsName, PowerState, AgentStatus, DomainName, DomainType, BastionHostId, PublicIpAddress, PrivateIpAddress, IsFavorite, LastConnectedAt, TagsJson)
            VALUES (@Id, @Name, @ResourceGroup, @SubscriptionId, @TenantId, @Location, @Kind, @OsType, @OsName, @PowerState, @AgentStatus, @DomainName, @DomainType, @BastionHostId, @PublicIpAddress, @PrivateIpAddress, @IsFavorite, @LastConnectedAt, @TagsJson)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                ResourceGroup = excluded.ResourceGroup,
                SubscriptionId = excluded.SubscriptionId,
                TenantId = excluded.TenantId,
                Location = excluded.Location,
                Kind = excluded.Kind,
                OsType = excluded.OsType,
                OsName = excluded.OsName,
                PowerState = excluded.PowerState,
                AgentStatus = excluded.AgentStatus,
                DomainName = excluded.DomainName,
                DomainType = excluded.DomainType,
                BastionHostId = excluded.BastionHostId,
                PublicIpAddress = excluded.PublicIpAddress,
                PrivateIpAddress = excluded.PrivateIpAddress,
                TagsJson = excluded.TagsJson;
        ";

        foreach (var machine in machines)
        {
            using var command = new SqliteCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@Id", machine.Id);
            command.Parameters.AddWithValue("@Name", machine.Name);
            command.Parameters.AddWithValue("@ResourceGroup", machine.ResourceGroup);
            command.Parameters.AddWithValue("@SubscriptionId", machine.SubscriptionId);
            command.Parameters.AddWithValue("@TenantId", machine.TenantId);
            command.Parameters.AddWithValue("@Location", machine.Location);
            command.Parameters.AddWithValue("@Kind", (int)machine.Kind);
            command.Parameters.AddWithValue("@OsType", machine.OsType);
            command.Parameters.AddWithValue("@OsName", machine.OsName);
            command.Parameters.AddWithValue("@PowerState", machine.PowerState);
            command.Parameters.AddWithValue("@AgentStatus", (object?)machine.AgentStatus ?? DBNull.Value);
            command.Parameters.AddWithValue("@DomainName", (object?)machine.DomainName ?? DBNull.Value);
            command.Parameters.AddWithValue("@DomainType", (int)machine.DomainType);
            command.Parameters.AddWithValue("@BastionHostId", (object?)machine.BastionHostId ?? DBNull.Value);
            command.Parameters.AddWithValue("@PublicIpAddress", (object?)machine.PublicIpAddress ?? DBNull.Value);
            command.Parameters.AddWithValue("@PrivateIpAddress", (object?)machine.PrivateIpAddress ?? DBNull.Value);
            command.Parameters.AddWithValue("@IsFavorite", machine.IsFavorite ? 1 : 0);
            command.Parameters.AddWithValue("@LastConnectedAt", machine.LastConnectedAt.HasValue ? machine.LastConnectedAt.Value.ToString("o") : (object)DBNull.Value);
            command.Parameters.AddWithValue("@TagsJson", JsonSerializer.Serialize(machine.Tags));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();
    }

    public async Task SetFavoriteAsync(string machineId, bool isFavorite, CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = "UPDATE Machines SET IsFavorite = @IsFavorite WHERE Id = @Id";
        using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", machineId);
        command.Parameters.AddWithValue("@IsFavorite", isFavorite ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RecordConnectionAsync(string machineId, CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = "UPDATE Machines SET LastConnectedAt = @Now WHERE Id = @Id";
        using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", machineId);
        command.Parameters.AddWithValue("@Now", DateTime.UtcNow.ToString("o"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StagecoachMachine>> GetRecentMachinesAsync(int count = 10, CancellationToken cancellationToken = default)
    {
        var list = new List<StagecoachMachine>();
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = "SELECT * FROM Machines WHERE LastConnectedAt IS NOT NULL ORDER BY LastConnectedAt DESC LIMIT @Count";
        using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@Count", count);
        using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(ReadMachine(reader));
        }

        return list;
    }

    private static StagecoachMachine ReadMachine(SqliteDataReader reader)
    {
        var tagsJson = reader["TagsJson"] as string;
        var tags = string.IsNullOrWhiteSpace(tagsJson) 
            ? new Dictionary<string, string>() 
            : (JsonSerializer.Deserialize<Dictionary<string, string>>(tagsJson) ?? new Dictionary<string, string>());

        var lastConnectedStr = reader["LastConnectedAt"] as string;
        DateTime? lastConnected = !string.IsNullOrWhiteSpace(lastConnectedStr) && DateTime.TryParse(lastConnectedStr, out var dt) ? dt : null;

        return new StagecoachMachine
        {
            Id = reader.GetString(reader.GetOrdinal("Id")),
            Name = reader.GetString(reader.GetOrdinal("Name")),
            ResourceGroup = reader.GetString(reader.GetOrdinal("ResourceGroup")),
            SubscriptionId = reader.GetString(reader.GetOrdinal("SubscriptionId")),
            TenantId = reader.GetString(reader.GetOrdinal("TenantId")),
            Location = reader.GetString(reader.GetOrdinal("Location")),
            Kind = (TargetKind)reader.GetInt32(reader.GetOrdinal("Kind")),
            OsType = reader.GetString(reader.GetOrdinal("OsType")),
            OsName = reader.GetString(reader.GetOrdinal("OsName")),
            PowerState = reader.GetString(reader.GetOrdinal("PowerState")),
            AgentStatus = reader["AgentStatus"] as string ?? string.Empty,
            DomainName = reader["DomainName"] as string ?? string.Empty,
            DomainType = (DomainType)reader.GetInt32(reader.GetOrdinal("DomainType")),
            BastionHostId = reader["BastionHostId"] as string,
            PublicIpAddress = reader["PublicIpAddress"] as string,
            PrivateIpAddress = reader["PrivateIpAddress"] as string,
            IsFavorite = reader.GetInt32(reader.GetOrdinal("IsFavorite")) == 1,
            LastConnectedAt = lastConnected,
            Tags = tags
        };
    }
}
