using System.Collections.Generic;
using System.Threading.Tasks;
using System.Data.SQLite;
using TShockAPI;
using System.IO;

namespace SmartRegions
{
    sealed class DBConnection
    {
        SQLiteConnection Connection;

        public void Initialize()
        {
            string path = Path.Combine(TShock.SavePath, "SmartRegions", "SmartRegions.sqlite");
            Connection = new SQLiteConnection($"Data Source={path}");
            Connection.Open();
            var command = new SQLiteCommand(
                "CREATE TABLE IF NOT EXISTS Regions (" +
                "  Name TEXT PRIMARY KEY," +
                "  Command TEXT," +
                "  Cooldown REAL)",
                Connection);
            command.ExecuteNonQuery();
        }

        public List<SmartRegion> GetRegions()
        {
            var result = new List<SmartRegion>();
            var command = new SQLiteCommand("SELECT Name, Command, Cooldown FROM Regions", Connection);
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
            var command = new SQLiteCommand(
                "INSERT OR REPLACE INTO Regions (Name, Command, Cooldown) VALUES (@nm, @cmd, @cool)",
                Connection);
            command.Parameters.Add(new SQLiteParameter("@nm", region.name));
            command.Parameters.Add(new SQLiteParameter("@cmd", region.command));
            command.Parameters.Add(new SQLiteParameter("@cool", region.cooldown));
            await command.ExecuteNonQueryAsync();
        }

        public async Task RemoveRegion(string region)
        {
            var command = new SQLiteCommand("DELETE FROM Regions WHERE Name = @nm", Connection);
            command.Parameters.Add(new SQLiteParameter("@nm", region));
            await command.ExecuteNonQueryAsync();
        }

        public void Close()
        {
            Connection?.Close();
        }
    }
}
