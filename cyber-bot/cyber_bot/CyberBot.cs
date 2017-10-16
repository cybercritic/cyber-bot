using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Discord;
using Discord.Commands;
using System.Threading.Tasks;
using System.Net.Http;
using System.IO;
using System.Net;
using System.Net.Http.Headers;
using System.Globalization;
using System.Timers;
using cyber_bot.Properties;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace cyber_bot.cyber_bot
{
    class CyberBot
    {
        private DiscordClient discordClient;
        private long lastAmount = 0;
        private DateTime lastRequest = new DateTime(0);
        private DateTime lastServerRequest = new DateTime(0);
        private string cookieValue = "INS_WEBSITE_COOKIE";

        private int ServerTick = 0;
        private int ServerTarget = 1;

        private List<CommandEventArgs> Alarms = new List<CommandEventArgs>();

        private Random rand = new Random((int)DateTime.UtcNow.Ticks);

        /// <summary>
        /// struct used for log file data
        /// </summary>
        struct InfoPoint
        {
            public DateTime Timestamp;
            public long Amount;
        }

        struct ServerPoint
        {
            public DateTime Timestamp;
            public long Players;
            public string ServerName; 
        }

        public CyberBot()
        {
            if (Settings.Default.masterCookie != null && Settings.Default.masterCookie.Length > 10)
                this.cookieValue = Settings.Default.masterCookie;

            //https://www.indiegogo.com/projects/infinity-battlescape#/backers

            //this.ParsePlayerNumber(Resources.test);
            //return;

            //string result = this.GetHTTPResponse().Result;

            //client config
            DiscordConfigBuilder config = new DiscordConfigBuilder();
            config.LogLevel = LogSeverity.Info;
            config.LogHandler = Log;
            config.ReconnectDelay = 1000 * 5;// 1000 * 60;
            config.FailedReconnectDelay = 1000 * 15;// 1000 * 60 * 5;
            config.ConnectionTimeout = 1000 * 30;// 1000 * 60 * 60;
            //config.EnablePreUpdateEvents = true;
            
            //client instance
            discordClient = new DiscordClient(config);
            
            //more config
            CommandServiceConfigBuilder cmdConfig = new CommandServiceConfigBuilder();
            cmdConfig.PrefixChar = '!';
            cmdConfig.AllowMentionPrefix = true;
            
            CommandServiceConfig cmdServCfg = cmdConfig.Build();
            discordClient.UsingCommands(cmdServCfg);
            
            //register commands
            this.RegisterCommands();

            //timer for funding check
            Timer stateTimer = new Timer(1000 * 60 * 60);
            stateTimer.Elapsed += OnTimerAmountCheck;
            stateTimer.Enabled = true;

            InfoPoint latest = this.ParseLogFile().Last();
            this.lastAmount = latest.Amount;
            this.lastRequest = DateTime.SpecifyKind(latest.Timestamp, DateTimeKind.Utc);

            if((DateTime.UtcNow - this.lastRequest).TotalMinutes >= 60)
                this.OnTimerAmountCheck(this, null);

            Timer serverTimer = new Timer(1000 * 60 * 1);
            serverTimer.Elapsed += OnTimerServerCheck;
            serverTimer.Enabled = true;

            this.OnTimerServerCheck(this, null);

            System.Threading.Thread.Sleep(1000);

            //connect
            try { discordClient.ExecuteAndWait(Connect); }
            catch { }
        }

        /// <summary>
        /// register commands here
        /// </summary>
        private void RegisterCommands()
        {
            CommandService cmdService = discordClient.GetService<CommandService>();
            cmdService.CreateCommand("hello").Do(HelloCommand);
            cmdService.CreateCommand("check").Do(CheckCommand);
            cmdService.CreateCommand("creator").Do(CreatorCommand);
            cmdService.CreateCommand("help").Do(HelpCommand);
            cmdService.CreateCommand("report").Do(ReportCommand);
            cmdService.CreateCommand("server").Do(ServerCommand);
            cmdService.CreateCommand("alarm").Do(AlarmCommand);
            cmdService.CreateCommand("echo").Do(EchoCommand);
            

            cmdService.CreateCommand("cookie").Parameter("cookie").Do(CookieCommand);
            cmdService.CreateCommand("quote").Parameter("username").Do(QuoteCommand);
            cmdService.CreateCommand("image").Parameter("topic").Do(ImageCommand);
        }

        private void OnTimerAmountCheck(object source, ElapsedEventArgs e)
        {
            this.ParseAmount(this.GetHTTPResponse().Result);
            this.AppendLogFile();
        }

        private void OnTimerServerCheck(object source, ElapsedEventArgs e)
        {
            this.ServerTick++;

            if (this.ServerTick >= this.ServerTarget)
            {
                List<ServerPoint> servers = this.ParsePlayerNumber(this.GetServersResponse().Result);
                this.AppendServerLogFile(servers);

                if (!servers.Exists(p => p.Players > 0))
                    this.ServerTarget++;
                else
                {
                    this.ServerTarget = 1;
                    if(this.Alarms.Count > 0)
                    {
                        List<Channel> visitedChannels = new List<Channel>();
                        foreach (CommandEventArgs alarm in this.Alarms)
                        {
                            if (visitedChannels.Exists(p => p.Id == alarm.Channel.Id))
                                continue;
                            visitedChannels.Add(alarm.Channel);

                            List<CommandEventArgs> subscribers = this.Alarms.FindAll(p => p.Channel.Id == alarm.Channel.Id);
                            string users = "";
                            foreach (CommandEventArgs sub in subscribers)
                                users += sub.User.Mention + " ";

                            alarm.Channel.SendMessage(string.Format("There is now a player on I:B server <{0}>, wake up {1}",servers.Find(p => p.Players > 0).ServerName,users));
                        }
                        Alarms.Clear();
                    }
                }

                if (this.ServerTarget > 15)
                    this.ServerTarget = 15;
                else if (this.ServerTarget <= 0)
                    this.ServerTarget = 1;

                this.ServerTick = 0;
            }
        }

        async Task<string> GetServersResponse()
        {
            //header stuff so we are not a bot
            Uri baseAddress = new Uri("http://inovaestudios.com");

            Cookie cookie = new Cookie(".AspNet.ApplicationCookie", this.cookieValue);

            CookieContainer cookieContainer = new CookieContainer();
            cookieContainer.Add(baseAddress, cookie);
            
            HttpClientHandler handler = new HttpClientHandler();
            handler.CookieContainer = cookieContainer;

            HttpClient client = new HttpClient(handler);
            client.BaseAddress = baseAddress;

            string getWebPage = "";
            try
            {
                //HttpResponseMessage response = await client.GetAsync("https://www.indiegogo.com/projects/infinity-battlescape#/backers");
                HttpResponseMessage response = await client.GetAsync("https://inovaestudios.com//api/v1/Servers/Index?productID=2");
                HttpContent content = response.Content;
                getWebPage = await content.ReadAsStringAsync();
            }
            catch { }

            return getWebPage;
        }

        private List<ServerPoint> ParsePlayerNumber(string webPage)
        {
            List<ServerPoint> result = new List<ServerPoint>();
            
            try
            {
                IBSserver[] json = JsonConvert.DeserializeObject<IBSserver[]>(webPage);

                foreach (IBSserver server in json)
                {
                    ServerPoint point = new ServerPoint();
                    point.ServerName = server.ServerName;
                    point.Timestamp = DateTime.UtcNow;
                    point.Players = server.Connections;
                    result.Add(point);
                }
            }
            catch
            {
                ServerPoint point = new ServerPoint();
                point.ServerName = "error";
                point.Players = -1;
                result.Add(point);
            }

            this.lastServerRequest = DateTime.UtcNow;
            return result;
        }

        private string GetRandomQuote(string username)
        {
            try
            {
                string url = string.Format("https://forums.inovaestudios.com/user_actions.json?offset=0&username={0}&filter=2", username);
                string webPage = this.GetHTTPResponse(url).Result;

                JObject tmp = JObject.Parse(webPage);
                //var json = JsonConvert.DeserializeObject(webPage);
                //var actions = JsonConvert.DeserializeObject(json["actions"]);

                List<string> quotes = new List<string>();
                foreach (JObject job in tmp["user_actions"])
                {
                    try
                    {
                        string s = job["excerpt"].ToString();
                        while (s.IndexOf("<") != -1)
                            s = s.Remove(s.IndexOf("<"), (s.IndexOf(">") + 1) - s.IndexOf("<"));
                        quotes.Add(WebUtility.HtmlDecode(s));
                    }
                    catch { }
                }

                if (quotes.Count > 0)
                    return quotes[this.rand.Next(quotes.Count)];
                else
                    return null;
            }
            catch
            {
                return null;
            }
        }

        async Task<string> GetHTTPResponse(string url)
        {
            //header stuff so we are not a bot

            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MOZILLA", "5.0"));
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(WINDOWS NT 6.1; WOW64)"));
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("APPLEWEBKIT", "537.1"));
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(KHTML, LIKE GECKO)"));
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CHROME", "21.0.1180.75"));
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SAFARI", "537.1"));

            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xhtml+xml"));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml", 0.9));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.8));

            Uri uri = new Uri(url);
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
          
            string getWebPage = "";
            try
            {
                HttpResponseMessage response = await client.SendAsync(request);

                HttpContent content = response.Content;
                getWebPage = await content.ReadAsStringAsync();
            }
            catch { }

            return getWebPage;
        }

        async Task<string> GetHTTPResponse()
        {
            //header stuff so we are not a bot
            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MOZILLA", "5.0"));
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(WINDOWS NT 6.1; WOW64)"));
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("APPLEWEBKIT", "537.1"));
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(KHTML, LIKE GECKO)"));
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CHROME", "21.0.1180.75"));
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SAFARI", "537.1"));

            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xhtml+xml"));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml", 0.9));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.8));

            //client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml");
            //client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 6.2; WOW64; rv:20.0) Gecko/20100101 Firefox/19.0");
            //client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Charset", "ISO-8859-1");

            string getWebPage = "";
            try
            {
                //HttpResponseMessage response = await client.GetAsync("https://www.indiegogo.com/projects/infinity-battlescape#/backers");
                HttpResponseMessage response = await client.GetAsync("https://www.indiegogo.com/project/infinity-battlescape/embedded");
                HttpContent content = response.Content;
                getWebPage = await content.ReadAsStringAsync();
            }
            catch { }

            return getWebPage;
        }

        /// <summary>
        /// write to log file
        /// </summary>
        private void AppendLogFile()
        {
            string write = "";
            write += DateTime.UtcNow.ToString("G", CultureInfo.InvariantCulture) + ";";
            write += this.lastAmount.ToString() + "\r\n";
            
            File.AppendAllText("log_file.txt", write, Encoding.Unicode);
            
            return;
        }

        /// <summary>
        /// write to log file
        /// </summary>
        private void AppendServerLogFile(List<ServerPoint> servers)
        {
            string write = "";
            foreach (ServerPoint server in servers)
            {
                write += DateTime.UtcNow.ToString("G", CultureInfo.InvariantCulture) + ";";
                write += server.ServerName + ";";
                write += server.Players.ToString() + "\r\n";
            }

            File.AppendAllText("server_log_file.txt", write, Encoding.Unicode);

            return;
        }

        /// <summary>
        /// parse the amount from indiegogo page
        /// </summary>
        /// <param name="webPage"></param>
        /// <returns></returns>
        private long ParseAmount(string webPage)
        {
            try
            {
                //parse amount
                int area = webPage.IndexOf("currency currency-medium");
                int startAmount = webPage.IndexOf('$', area);
                int endAmount = webPage.IndexOf('<', startAmount);

                string strAmount = webPage.Substring(startAmount + 1, endAmount - (startAmount + 1));
                strAmount = strAmount.Replace(",", String.Empty);
                long funding = 0;
                if (!long.TryParse(strAmount, out funding))
                    return long.MinValue;
                else
                    this.lastAmount = funding;
            }
            catch
            {
                //something went wrong, probably robot ban
                if (this.lastRequest.Ticks == 0)
                    return long.MinValue;
                else
                    return long.MaxValue;
            }

            this.lastRequest = DateTime.UtcNow;
            return this.lastAmount;
        }

        async Task CookieCommand(CommandEventArgs e)
        {
            this.cookieValue = e.GetArg("cookie");
            Settings.Default.masterCookie = this.cookieValue;
            Settings.Default.Save();
            
            await e.Channel.SendMessage("new cookie set.");
        }

        async Task QuoteCommand(CommandEventArgs e)
        {
            await e.Channel.SendMessage("looking...");

            string username = e.GetArg("username");
            string quote = this.GetRandomQuote(username);

            if (quote == null)
                await e.Channel.SendMessage("Something went wrong, sorry");
            else if(quote.Length == 0)
                await e.Channel.SendMessage(string.Format("Nothing found, sorry."));
            else
                await e.Channel.SendMessage(string.Format("```{0}```\n{1}", quote, username));
        }

        async Task EchoCommand(CommandEventArgs e)
        {
            await e.Channel.SendMessage("echo " + e.User.Mention);
        }

        async Task AlarmCommand(CommandEventArgs e)
        {
            await e.Channel.SendMessage("setting a once-off I:B player alarm for this channel...");

            if(!this.Alarms.Exists(p => p.Channel.Id == e.Channel.Id && p.User.Name == e.User.Name))
                this.Alarms.Add(e);
        }

        async Task ImageCommand(CommandEventArgs e)
        {
            await e.Channel.SendMessage("looking...");

            string topic = e.GetArg("topic");
            string ext = "";

            GoogleImage googleImg = new GoogleImage();
            //MemoryStream stream = googleImg.GetFinalImage(topic, out ext);
            string link = googleImg.GetFinalImageURL(topic);
            await e.Channel.SendMessage(link);
        }

        async Task ServerCommand(CommandEventArgs e)
        {
            await e.Channel.SendMessage("querying server...");

            List<ServerPoint> servers = this.ParsePlayerNumber(this.GetServersResponse().Result);

            string output = "";
            foreach (ServerPoint server in servers)
                output += string.Format("{0}\nplayers:{1}\n", server.ServerName, server.Players);

            if(output.Length == 0)
                await e.Channel.SendMessage(string.Format("Looks like the server is down."));
            else
                await e.Channel.SendMessage(string.Format("```{0}```", output));

            //this.AppendLogFile();
        }

        async Task CheckCommand(CommandEventArgs e)
        {
            //only allow one request per hour
            if(this.lastRequest.Ticks != 0 && (DateTime.UtcNow - this.lastRequest).TotalMinutes < 60)
            {
                await e.Channel.SendMessage("$" + this.lastAmount.ToString("N0") + " at [" + this.lastRequest.ToUniversalTime().ToShortTimeString() +" UTC]");
                return;
            }

            await e.Channel.SendMessage("checking...");

            string getWebPage = await GetHTTPResponse();

            long funding = this.ParseAmount(getWebPage);

            await e.Channel.SendMessage("$" + this.lastAmount.ToString("N0") + " at [" + this.lastRequest.ToUniversalTime().ToShortTimeString() + " UTC]");

            this.AppendLogFile();
        }

        async Task ReportCommand(CommandEventArgs e)
        {
            await e.Channel.SendMessage("calculating...");

            List<InfoPoint> data = this.ParseLogFile();

            //yes it's ugly
            DateTime nowDate = DateTime.UtcNow;
            InfoPoint latest = this.ClosestPoint(data, nowDate);
            InfoPoint hour = this.ClosestPoint(data, nowDate.AddHours(-12));
            InfoPoint day = this.ClosestPoint(data, nowDate.AddDays(-1));
            InfoPoint week = this.ClosestPoint(data, nowDate.AddDays(-7));
            InfoPoint month1 = this.ClosestPoint(data, nowDate.AddMonths(-1));
            InfoPoint month2 = this.ClosestPoint(data, nowDate.AddMonths(-2));

            string report = "date\t\t\t\tamount\t\t  difference\n";
            report += "[" + latest.Timestamp.ToShortTimeString() + " UTC]";
            report += "\t\t $" + latest.Amount.ToString("N0");
            report += "\t\t$" + "0" + "\n";

            if (latest.Timestamp != hour.Timestamp)
            {
                report += "[" + hour.Timestamp.ToShortTimeString() + " UTC]";
                report += "\t\t $" + hour.Amount.ToString("N0");
                report += "\t\t$" + (latest.Amount - hour.Amount).ToString("N0") + "\n";
            }

            if ((hour.Timestamp != day.Timestamp) && (hour.Timestamp - day.Timestamp).TotalHours > 6)
            {
                report += "[" + day.Timestamp.ToShortDateString() + "]";
                report += "\t\t$" + day.Amount.ToString("N0");
                report += "\t\t$" + (latest.Amount - day.Amount).ToString("N0") + "\n";
            }

            if ((day.Timestamp != week.Timestamp) && (day.Timestamp - week.Timestamp).TotalDays > 3)
            {
                report += "[" + week.Timestamp.ToShortDateString() + "]";
                report += "\t\t$" + week.Amount.ToString("N0");
                report += "\t\t$" + (latest.Amount - week.Amount).ToString("N0") + "\n";
            }

            if ((week.Timestamp != month1.Timestamp) && (week.Timestamp - month1.Timestamp).TotalDays > 15)
            {
                report += "[" + month1.Timestamp.ToShortDateString() + "]";
                report += "\t\t$" + month1.Amount.ToString("N0");
                report += "\t\t$" + (latest.Amount - month1.Amount).ToString("N0") + "\n";
            }

            if ((month1.Timestamp != month2.Timestamp) && (month1.Timestamp - month2.Timestamp).TotalDays > 15)
            {
                report += "[" + month2.Timestamp.ToShortDateString() + "]";
                report += "\t\t$" + month2.Amount.ToString("N0");
                report += "\t\t$" + (latest.Amount - month2.Amount).ToString("N0");
            }

            await e.Channel.SendMessage("```" + report + "```");
        }

        /// <summary>
        /// get the closest datetime, assuming it's sorted as loaded from file
        /// </summary>
        /// <param name="data"></param>
        /// <param name="target"></param>
        /// <returns></returns>
        private InfoPoint ClosestPoint(List<InfoPoint> data, DateTime target)
        {
            if (data.Count == 0)
                return new InfoPoint();

            InfoPoint previous = new InfoPoint();
            for (int i = data.Count - 1; i >= 0; i--)
            {
                InfoPoint current = data[i];
                if (current.Timestamp < target && previous.Timestamp > target)
                    return Math.Abs(current.Timestamp.Ticks - target.Ticks) < Math.Abs(previous.Timestamp.Ticks - target.Ticks) ? current : previous;
                else if (current.Timestamp < target && previous.Timestamp < target)
                    return current;

                previous = current;
            }

            return previous;
        }

        /// <summary>
        /// parse logfile data
        /// </summary>
        /// <returns></returns>
        private List<ServerPoint> ParseServerLogFile()
        {
            List<ServerPoint> result = new List<ServerPoint>();

            string[] logLines = File.ReadAllLines("server_log_file.txt", Encoding.Unicode);
            foreach (string line in logLines)
            {
                try
                {
                    string[] parts = line.Trim().Split(';');
                    ServerPoint current = new ServerPoint();
                    current.Timestamp = DateTime.ParseExact(parts[0], "G", CultureInfo.InvariantCulture);
                    current.ServerName = parts[1];
                    current.Players = long.Parse(parts[2]);

                    result.Add(current);
                }
                catch { }
            }

            return result;
        }

        /// <summary>
        /// parse logfile data
        /// </summary>
        /// <returns></returns>
        private List<InfoPoint> ParseLogFile()
        {
            List<InfoPoint> result = new List<InfoPoint>();

            string[] logLines = File.ReadAllLines("log_file.txt", Encoding.Unicode);
            foreach(string line in logLines)
            {
                try
                {
                    string[] parts = line.Trim().Split(';');
                    InfoPoint current = new InfoPoint();
                    current.Timestamp = DateTime.ParseExact(parts[0], "G", CultureInfo.InvariantCulture);
                    current.Amount = long.Parse(parts[1]);

                    result.Add(current);
                }
                catch { }
            }

            return result;
        }

        async Task HelloCommand(CommandEventArgs e)
        {
            await e.Channel.SendMessage("o/");
        }

        async Task CreatorCommand(CommandEventArgs e)
        {
            await e.Channel.SendMessage("my creator is critic");
        }

        async Task HelpCommand(CommandEventArgs e)
        {
            await e.Channel.SendMessage("```I support the following commands: help, hello, creator, check, report, server, alarm, quote <username>, image <topic>```");
        }

        async Task Connect()
        {
            //222670956630507522
            //https://discordapp.com/oauth2/authorize?client_id=222670956630507522&scope=bot&permissions=0
            //you need your own code, check here
            //https://youtu.be/oE6alzUzcw4
            await discordClient.Connect("DISCORD_BOT_AUTHENTICATION_TOKEN", TokenType.Bot);
        }

        private void Log(object sender, LogMessageEventArgs e)
        {

            Console.WriteLine(e.Message);
        }
    }
}

//admirer
//fascinated

//https://inovaestudios.blob.core.windows.net/forumsavatars/original/2X/4/4f578eff8395c9ffe670827946c66cc009d71e47.gif
