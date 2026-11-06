using TShockAPI;
using TShockAPI.DB;

namespace SmartRegions
{
    public class SmartRegion
    {
        private Region _region = null;

        public string name;
        public string command;
        public double cooldown;
        /// <summary>
        /// The group of whoever set the region, whose permissions its commands run with. Null to run them
        /// as the server.
        /// </summary>
        public string group;
        public Region region
        {
            get
            {
                if (_region == null)
                {
                    _region = TShock.Regions.GetRegionByName(name);
                }
                return _region;
            }
        }
    }
}
