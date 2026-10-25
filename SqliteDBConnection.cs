using System;
using System.Data.Common;
using System.IO;
using Microsoft.Data.Sqlite;
using TShockAPI;

namespace SmartRegions
{
    sealed class SqliteDBConnection : DBConnection
    {
        static string FilePath => Path.Combine(TShock.SavePath, "SmartRegions", "SmartRegions.sqlite");

        public static bool Exists => File.Exists(FilePath);

        public static void MoveToBackup()
        {
            string backupPath = FilePath + ".backup";
            try
            {
                File.Move(FilePath, backupPath, overwrite: true);
            }
            catch (FileNotFoundException)
            {
                // Another server sharing this file already moved it
                return;
            }
            TShock.Log.ConsoleInfo(
                "Legacy SQLite databse SmartRegions.sqlite renamed to SmartRegions.sqlite.backup.");
        }

        readonly bool readOnly;

        public SqliteDBConnection(bool readOnly)
        {
            this.readOnly = readOnly;
        }

        protected override string RegionsTable => "Regions";
        protected override string LastUpdateTable => "LastUpdate";
        protected override string[] CreateTablesSql => new[]
        {
            "CREATE TABLE IF NOT EXISTS Regions (" +
            "  Name TEXT PRIMARY KEY," +
            "  Command TEXT," +
            "  Cooldown REAL)",
            "CREATE TABLE IF NOT EXISTS LastUpdate (" +
            "  Id INTEGER PRIMARY KEY," +
            "  LastUpdate TEXT NOT NULL)"
        };
        protected override string TableExistsSql =>
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name";

        protected override DbConnection CreateConnection()
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = FilePath,
                Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
                // Don't keep the file open between operations
                Pooling = false
            };
            return new SqliteConnection(builder.ToString());
        }

        protected override DateTime? UnrecordedLastUpdate()
        {
            return WholeSeconds(File.GetLastWriteTimeUtc(FilePath));
        }
    }
}
