using System.Data.Common;
using MySql.Data.MySqlClient;
using TShockAPI;

namespace SmartRegions
{
    sealed class MySqlDBConnection : DBConnection
    {
        public static bool IsConfigured =>
            TShock.Config.Settings.MySqlConnectionString is not (null or "")
            || TShock.Config.Settings.MySqlDbName is not (null or "");

        protected override string RegionsTable => "smartregions";
        protected override string LastUpdateTable => "smartregions_lastupdate";
        protected override string[] CreateTablesSql => new[]
        {
            "CREATE TABLE IF NOT EXISTS smartregions (" +
            "  Name VARCHAR(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin PRIMARY KEY," +
            "  Command TEXT CHARACTER SET utf8mb4," +
            "  Cooldown DOUBLE," +
            "  GroupName VARCHAR(255) CHARACTER SET utf8mb4)",
            "CREATE TABLE IF NOT EXISTS smartregions_lastupdate (" +
            "  Id INT PRIMARY KEY," +
            "  LastUpdate TIMESTAMP NOT NULL)"
        };
        protected override string TableExistsSql =>
            "SELECT COUNT(*) FROM information_schema.tables " +
            "WHERE table_schema = DATABASE() AND table_name = @name";
        protected override string ColumnExistsSql =>
            "SELECT COUNT(*) FROM information_schema.columns " +
            "WHERE table_schema = DATABASE() AND table_name = @table AND column_name = @column";
        protected override string AddGroupColumnSql =>
            "ALTER TABLE smartregions ADD COLUMN GroupName VARCHAR(255) CHARACTER SET utf8mb4";
        // TIMESTAMP values are converted using the session time zone; UTC avoids DST gaps and overlaps
        protected override string ConnectionInitSql => "SET time_zone = '+00:00'";

        /// <summary> Connects with TShock's MySQL settings, the same way TShock does </summary>
        protected override DbConnection CreateConnection()
        {
            var settings = TShock.Config.Settings;
            if (settings.MySqlConnectionString is not (null or ""))
            {
                var connectionString = new MySqlConnectionStringBuilder(settings.MySqlConnectionString);
                return new MySqlConnection(connectionString.ToString());
            }

            string[] hostport = settings.MySqlHost.Split(':');
            var builder = new MySqlConnectionStringBuilder
            {
                Server = hostport[0],
                Port = hostport.Length > 1 ? uint.Parse(hostport[1]) : 3306,
                Database = settings.MySqlDbName,
                UserID = settings.MySqlUsername,
                Password = settings.MySqlPassword
            };
            return new MySqlConnection(builder.ToString());
        }
    }
}
