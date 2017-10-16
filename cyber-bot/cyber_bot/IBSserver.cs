using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace cyber_bot.cyber_bot
{
    public class IBSserver
    {
        public string SessionID { get; set; }
        public string ProductID { get; set; }
        public string StartTime { get; set; }
        public string LastUpTime { get; set; }
        public string ServerName { get; set; }
        public string ServerDescr { get; set; }
        public string HostName { get; set; }
        public string Port { get; set; }
        public int Connections { get; set; }
        public string MaxConnections { get; set; }
        public string OwnerName { get; set; }
        public string MinClientVersion { get; set; }
        public string ServerVersion { get; set; }
    }
}
