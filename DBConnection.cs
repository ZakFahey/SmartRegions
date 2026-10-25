﻿using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using TShockAPI;

namespace SmartRegions
{
    abstract class DBConnection
    {
        protected abstract string RegionsTable { get; }
        protected abstract string LastUpdateTable { get; }
        protected abstract string[] CreateTablesSql { get; }
        /// <summary> Query returning the number of tables named @name </summary>
        protected abstract string TableExistsSql { get; }
        protected virtual string ConnectionInitSql => null;

        protected abstract DbConnection CreateConnection();

        /// <summary> The last update time of a store that has regions but no recorded update time </summary>
        protected virtual DateTime? UnrecordedLastUpdate() => null;

        /// <summary>
        /// Creates a DB connection based on either MySQL or SQLite, based on the data store and credentials
        /// used in TShock's config.json.
        /// </summary>
        public static DBConnection Open()
        {
            bool useMySql = TShock.Config.Settings.StorageType.Equals("mysql", StringComparison.OrdinalIgnoreCase);
            DBConnection store = useMySql ? new MySqlDBConnection() : new SqliteDBConnection(readOnly: false);
            DBConnection other = null;
            if (useMySql && SqliteDBConnection.Exists)
            {
                other = new SqliteDBConnection(readOnly: true);
            }
            else if (!useMySql && MySqlDBConnection.IsConfigured)
            {
                other = new MySqlDBConnection();
            }

            DateTime? lastUpdate = store.GetLastUpdate();
            store.CreateTables();
            if (other != null)
            {
                try
                {
                    // If an older data store exists and we're switching to the other (as determined by the
                    // last update time of both), then migrate the data to the new data store.
                    DateTime? otherLastUpdate = other.GetLastUpdate();
                    if (otherLastUpdate != null && (lastUpdate == null || otherLastUpdate > lastUpdate))
                    {
                        TShock.Log.ConsoleInfo(
                            $"Migrating region storage to {(useMySql ? "MySQL" : "SQLite")}.");
                        store.ReplaceAll(other.GetRegions(), otherLastUpdate.Value);
                    }
                    if (useMySql)
                    {
                        SqliteDBConnection.MoveToBackup();
                    }
                }
                catch (Exception e)
                {
                    TShock.Log.Error(e.ToString());
                }
            }
            return store;
        }

        public List<SmartRegion> GetRegions()
        {
            var result = new List<SmartRegion>();
            using var connection = OpenConnection();
            using var command = Command(connection, null, $"SELECT Name, Command, Cooldown FROM {RegionsTable}");
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    string name = reader.GetString(0);
                    string cmd = reader.GetString(1);
                    double cooldown = reader.GetDouble(2);
                    result.Add(new SmartRegion { name = name, command = cmd, cooldown = cooldown });
                }
            }
            return result;
        }

        public async Task SaveRegion(SmartRegion region)
        {
            await using var connection = await OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using (var command = UpsertCommand(connection, transaction, region))
            {
                await command.ExecuteNonQueryAsync();
            }
            await using (var command = SetLastUpdateCommand(connection, transaction, DateTime.UtcNow))
            {
                await command.ExecuteNonQueryAsync();
            }
            await transaction.CommitAsync();
        }

        public async Task RemoveRegion(string region)
        {
            await using var connection = await OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using (var command = Command(connection, transaction,
                $"DELETE FROM {RegionsTable} WHERE Name = @nm", ("@nm", region)))
            {
                await command.ExecuteNonQueryAsync();
            }
            await using (var command = SetLastUpdateCommand(connection, transaction, DateTime.UtcNow))
            {
                await command.ExecuteNonQueryAsync();
            }
            await transaction.CommitAsync();
        }

        void CreateTables()
        {
            using var connection = OpenConnection();
            foreach (string sql in CreateTablesSql)
            {
                using var command = Command(connection, null, sql);
                command.ExecuteNonQuery();
            }
        }

        /// <returns> When the regions were last changed, or null if this store has no regions </returns>
        DateTime? GetLastUpdate()
        {
            using var connection = OpenConnection();
            if (!TableExists(connection, RegionsTable))
            {
                return null;
            }
            if (TableExists(connection, LastUpdateTable))
            {
                using var command = Command(connection, null, $"SELECT LastUpdate FROM {LastUpdateTable}");
                using var reader = command.ExecuteReader();
                if (reader.Read())
                {
                    return DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc);
                }
            }
            // If the last update table doesn't exist, fall back to other ways of figuring it out.
            using var countCommand = Command(connection, null, $"SELECT COUNT(*) FROM {RegionsTable}");
            return Convert.ToInt64(countCommand.ExecuteScalar()) > 0 ? UnrecordedLastUpdate() : null;
        }

        void ReplaceAll(List<SmartRegion> regions, DateTime lastUpdate)
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            using (var command = Command(connection, transaction, $"DELETE FROM {RegionsTable}"))
            {
                command.ExecuteNonQuery();
            }
            foreach (var region in regions)
            {
                using var command = UpsertCommand(connection, transaction, region);
                command.ExecuteNonQuery();
            }
            using (var command = SetLastUpdateCommand(connection, transaction, lastUpdate))
            {
                command.ExecuteNonQuery();
            }
            transaction.Commit();
        }

        bool TableExists(DbConnection connection, string table)
        {
            using var command = Command(connection, null, TableExistsSql, ("@name", table));
            return Convert.ToInt64(command.ExecuteScalar()) > 0;
        }

        DbCommand UpsertCommand(DbConnection connection, DbTransaction transaction, SmartRegion region)
        {
            return Command(connection, transaction,
                $"REPLACE INTO {RegionsTable} (Name, Command, Cooldown) VALUES (@nm, @cmd, @cool)",
                ("@nm", region.name), ("@cmd", region.command), ("@cool", region.cooldown));
        }

        DbCommand SetLastUpdateCommand(DbConnection connection, DbTransaction transaction, DateTime time)
        {
            return Command(connection, transaction,
                $"REPLACE INTO {LastUpdateTable} (Id, LastUpdate) VALUES (0, @time)",
                ("@time", WholeSeconds(time)));
        }

        /// <summary> Times are stored in whole seconds so they survive a round trip through either store </summary>
        protected static DateTime WholeSeconds(DateTime time)
        {
            return new DateTime(time.Ticks - time.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
        }

        DbConnection OpenConnection()
        {
            var connection = CreateConnection();
            connection.Open();
            if (ConnectionInitSql != null)
            {
                using var command = Command(connection, null, ConnectionInitSql);
                command.ExecuteNonQuery();
            }
            return connection;
        }

        async Task<DbConnection> OpenConnectionAsync()
        {
            var connection = CreateConnection();
            await connection.OpenAsync();
            if (ConnectionInitSql != null)
            {
                await using var command = Command(connection, null, ConnectionInitSql);
                await command.ExecuteNonQueryAsync();
            }
            return connection;
        }

        static DbCommand Command(DbConnection connection, DbTransaction transaction, string sql,
            params (string name, object value)[] parameters)
        {
            var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Transaction = transaction;
            foreach (var (name, value) in parameters)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = name;
                parameter.Value = value;
                command.Parameters.Add(parameter);
            }
            return command;
        }
    }
}
