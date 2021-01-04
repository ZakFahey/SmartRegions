using System;
using System.Collections.Generic;
using System.Linq;
using TShockAPI;
using Terraria;
using TerrariaApi.Server;
using System.IO;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace SmartRegions
{
    [ApiVersion(2, 1)]
    public class Plugin : TerrariaPlugin
    {
        private DBConnection DBConnection;
        List<SmartRegion> regions;
        PlayerData[] players = new PlayerData[255];
        struct PlayerData
        {
            public Dictionary<SmartRegion, DateTime> cooldowns;
            public SmartRegion regionToReplace;
            public void Reset()
            {
                cooldowns = new Dictionary<SmartRegion, DateTime>();
                regionToReplace = null;
            }
        }

        public Plugin(Main game) : base(game) { }
        public override void Initialize()
        {
            Commands.ChatCommands.Add(new Command("SmartRegions.manage", regionCommand, "smartregion"));
            Commands.ChatCommands.Add(new Command("SmartRegions.manage", replaceRegion, "replace"));

            ServerApi.Hooks.NetGreetPlayer.Register(this, OnGreetPlayer);
            ServerApi.Hooks.GameUpdate.Register(this, OnUpdate);

            DBConnection = new DBConnection();
            DBConnection.Initialize();
            string folder = Path.Combine(TShock.SavePath, "SmartRegions");
            if(!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
            else
            {
                ReplaceLegacyRegionStorage();
            }
            regions = DBConnection.GetRegions();

        }
        protected override void Dispose(bool Disposing)
        {
            if(Disposing)
            {
                ServerApi.Hooks.NetGreetPlayer.Deregister(this, OnGreetPlayer);
                ServerApi.Hooks.GameUpdate.Deregister(this, OnUpdate);
            }
            base.Dispose(Disposing);
        }
        public override Version Version
        {
            get { return new Version("1.2.1"); }
        }
        public override string Name
        {
            get { return "Smart Regions"; }
        }
        public override string Author
        {
            get { return "GameRoom"; }
        }
        public override string Description
        {
            get { return "Runs commands when players enter a region."; }
        }

        private void OnGreetPlayer(GreetPlayerEventArgs args)
        {
            players[args.Who].Reset();
        }

        void OnUpdate(EventArgs args)
        {
            foreach(TSPlayer player in TShock.Players)
                if(player != null && NetMessage.buffer[player.Index].broadcast)
                {
                    var inRegion = TShock.Regions.InAreaRegionName((int)(player.X / 16), (int)(player.Y / 16));
                    var hs = new HashSet<string>(inRegion);
                    var inSmartRegion = regions.Where(x => hs.Contains(x.name)).OrderByDescending(x => x.region.Z);

                    int regionCounter = 0;
                    foreach(var region in inSmartRegion)
                    {
                        if((regionCounter++ == 0 || !region.region.Name.EndsWith("--"))
                            && (!players[player.Index].cooldowns.ContainsKey(region)
                                || DateTime.UtcNow > players[player.Index].cooldowns[region]))
                        {
                            string file = Path.Combine(TShock.SavePath, "SmartRegions", region.command);
                            if(File.Exists(file))
                            {
                                foreach(string command in File.ReadAllLines(file))
                                {
                                    Commands.HandleCommand(TSPlayer.Server, replaceWithName(command, player));
                                }
                            }
                            else
                            {
                                Commands.HandleCommand(TSPlayer.Server, replaceWithName(region.command, player));
                            }
                            if(players[player.Index].cooldowns.ContainsKey(region))
                            {
                                players[player.Index].cooldowns[region] = DateTime.UtcNow.AddSeconds(region.cooldown);
                            }
                            else
                            {
                                players[player.Index].cooldowns.Add(region, DateTime.UtcNow.AddSeconds(region.cooldown));
                            }
                        }
                    }
                }
        }

        string replaceWithName(string cmd, TSPlayer player)
        {
            return cmd.Replace("[PLAYERNAME]", "\"" + player.Name + "\"");
        }

        public async void regionCommand(CommandArgs args)
        {
            try
            {
                await regionCommandInner(args);
            }
            catch (Exception e)
            {
                TShock.Log.Error(e.ToString());
                args.Player.SendErrorMessage("The command threw an error.");
            }
        }

        public async Task regionCommandInner(CommandArgs args)
        {
            switch(args.Parameters.ElementAtOrDefault(0))
            {
                case "add":
                    {
                        if(args.Parameters.Count < 4)
                        {
                            args.Player.SendErrorMessage("Invalid syntax! Correct syntax: /smartregion add <region name> <cooldown> <command or file>");
                        }
                        else
                        {
                            double cooldown = 0;
                            if(!double.TryParse(args.Parameters[2], out cooldown))
                            {
                                args.Player.SendErrorMessage("Invalid syntax! Correct syntax: /smartregion add <region name> <cooldown> <command or file>");
                                return;
                            }
                            string command = string.Join(" ", args.Parameters.GetRange(3, args.Parameters.Count - 3));
                            if(!TShock.Regions.Regions.Exists(x => x.Name == args.Parameters[1]))
                            {
                                args.Player.SendErrorMessage("The region {0} doesn't exist!", args.Parameters[1]);
                                IEnumerable<string> regionNames = from region_ in TShock.Regions.Regions
                                                                  where region_.WorldID == Main.worldID.ToString()
                                                                  select region_.Name;
                                PaginationTools.SendPage(args.Player, 1, PaginationTools.BuildLinesFromTerms(regionNames),
                                    new PaginationTools.Settings
                                    {
                                        HeaderFormat = "Regions ({0}/{1}):",
                                        FooterFormat = "Type {0}region list {{0}} for more.".SFormat(Commands.Specifier),
                                        NothingToDisplayString = "There are currently no regions defined."
                                    });
                            }
                            else
                            {
                                string cmdName = "";
                                for(int i = 1; i < command.Length && command[i] != ' '; i++)
                                {
                                    cmdName += command[i];
                                }
                                Command cmd = Commands.ChatCommands.FirstOrDefault(c => c.HasAlias(cmdName));
                                if(cmd != null && !cmd.CanRun(args.Player))
                                {
                                    args.Player.SendErrorMessage("You cannot create a smart region with a command you don't have permission to use yourself!");
                                    return;
                                }
                                if(cmd != null && !cmd.AllowServer)
                                {
                                    args.Player.SendErrorMessage("Your command must be usable by the server!");
                                    return;
                                }

                                var existingRegion = regions.FirstOrDefault(x => x.name == args.Parameters[1]);
                                var newRegion = new SmartRegion
                                {
                                    name = args.Parameters[1],
                                    cooldown = cooldown,
                                    command = command
                                };
                                if(existingRegion != null)
                                {
                                    players[args.Player.Index].regionToReplace = newRegion;
                                    args.Player.SendErrorMessage("The smart region {0} already exists! Type /replace to replace it.", args.Parameters[1]);
                                }
                                else
                                {
                                    regions.Add(newRegion);
                                    await DBConnection.SaveRegion(newRegion);
                                    args.Player.SendSuccessMessage("Smart region added!");
                                }
                            }
                        }
                    }
                    break;
                case "remove":
                    {
                        if(args.Parameters.Count != 2)
                        {
                            args.Player.SendErrorMessage("Invalid syntax! Correct syntax: /smartregion remove <regionname>");
                        }
                        else
                        {
                            var region = regions.FirstOrDefault(x => x.name == args.Parameters[1]);
                            if(region == null)
                            {
                                args.Player.SendErrorMessage("No such smart region exists!");
                            }
                            else
                            {
                                regions.Remove(region);
                                await DBConnection.RemoveRegion(region.name);
                                args.Player.SendSuccessMessage("The smart region {0} was removed!", args.Parameters[1]);
                            }
                        }
                    }
                    break;
                case "check":
                    {
                        if(args.Parameters.Count != 2)
                        {
                            args.Player.SendErrorMessage("Invalid syntax! Correct syntax: /smartregion check <regionname>");
                        }
                        else
                        {
                            var region = regions.FirstOrDefault(x => x.name == args.Parameters[1]);
                            if(region == null)
                            {
                                args.Player.SendInfoMessage("That region doesn't have a command associated with it.");
                            }
                            else
                            {
                                string file = Path.Combine(TShock.SavePath, "SmartRegions", region.command), commands;
                                if(File.Exists(file)) commands = "s:\n" + File.ReadAllText(file);
                                else commands = ":\n" + region.command;
                                args.Player.SendInfoMessage("The region {0} has a cooldown of {1} second{2} and uses the command{3}", args.Parameters[1], region.cooldown, region.cooldown == 1.0 ? "" : "s", commands);
                            }
                        }
                    }
                    break;
                case "list":
                    {
                        int pageNumber = 1;
                        int maxDist = int.MaxValue;
                        if(args.Parameters.Count > 1)
                        {
                            int.TryParse(args.Parameters[1], out pageNumber);
                        }
                        if(args.Parameters.Count > 2)
                        {
                            if(args.Player == TSPlayer.Server)
                            {
                                args.Player.SendErrorMessage("You cannot use the distance argument if you're the server.");
                                return;
                            }
                            int.TryParse(args.Parameters[2], out maxDist);
                        }
                        List<SmartRegion> regionList = regions;
                        if(maxDist < int.MaxValue)
                        {
                            regionList = regionList
                                .Where(r => r.region != null && Vector2.Distance(args.Player.TPlayer.position, r.region.Area.Center() * 16) < maxDist * 16)
                                .ToList();
                        }
                        List<string> regionNames = regionList.Select(r => r.name).ToList();
                        regionNames.Sort();

                        if(regionNames.Count == 0)
                        {
                            string suffix = "";
                            if(maxDist < int.MaxValue)
                            {
                                suffix = " nearby";
                            }
                            args.Player.SendErrorMessage($"There are no smart regions{suffix}.");
                        }
                        else
                        {
                            PaginationTools.SendPage(
                                args.Player, pageNumber, PaginationTools.BuildLinesFromTerms(regionNames),
                                new PaginationTools.Settings
                                {
                                    HeaderFormat = "Smart regions ({0}/{1}):",
                                    FooterFormat = string.Format("Type {0}smartregion list {{0}} for more.", Commands.Specifier)
                                }
                            );
                        }
                    }
                    break;
                default:
                    {
                        args.Player.SendInfoMessage("/smartregion sub-commands:\nadd <region name> <cooldown> <command or file>\nremove <region name>\ncheck <region name>\nlist [page] [max dist]");
                    }
                    break;
            }
        }

        void ReplaceLegacyRegionStorage()
        {
            string path = Path.Combine(TShock.SavePath, "SmartRegions", "config.txt");
            if(File.Exists(path))
            {
                var tasks = new List<Task>();
                try
                {
                    string[] lines = File.ReadAllLines(path);
                    for(int i = 0; i < lines.Length; i += 3)
                    {
                        var task = DBConnection.SaveRegion(new SmartRegion
                        {
                            name = lines[i],
                            command = lines[i + 1],
                            cooldown = double.Parse(lines[i + 2])
                        });
                        tasks.Add(task);
                    }
                    Task.WaitAll(tasks.ToArray());
                    File.Delete(path);
                }
                catch(Exception e)
                {
                    TShock.Log.Error(e.ToString());
                }
            }
        }

        public async void replaceRegion(CommandArgs args)
        {
            try
            {
                if (players[args.Player.Index].regionToReplace == null)
                {
                    args.Player.SendErrorMessage("You can't do that right now!");
                }
                else
                {
                    regions.RemoveAll(x => x.name == players[args.Player.Index].regionToReplace.name);
                    regions.Add(players[args.Player.Index].regionToReplace);
                    await DBConnection.SaveRegion(players[args.Player.Index].regionToReplace);
                    players[args.Player.Index].regionToReplace = null;
                    args.Player.SendSuccessMessage("Region successfully replaced!");
                }
            }
            catch (Exception e)
            {
                TShock.Log.Error(e.ToString());
                args.Player.SendErrorMessage("The command threw an error.");
            }
        }
    }
}