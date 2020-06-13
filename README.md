This plugin runs any command you want when a player enters a region. You could use this to set a player's team, heal them, give them items, or whatever you want. The possibilities are endless.

# Commands:
/smartregion add <region name> <cooldown> <command or file>: sets an existing region to a command. The cooldown represents how long the interval is between activations. You can do a command or make a file with a series of commands (each separated by a new line) in tshock/SmartRegions/. You can also use [PLAYERNAME], and the plugin will replace that with the player that is in the region. Example:
/smartregion add blueteam 100 /tteam [PLAYERNAME] blue
/smartregion remove <region name>
/smartregion check <region name>: Displays info about a smart region.
/smartregion list [page] [distance]: Lists all smart regions.

# Permissions
SmartRegions.manage

End a region name with `--` and it will only execute if it has the highest Z value of all overlapping regions.
