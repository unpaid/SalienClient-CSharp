using System;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Web;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SalienClientManager
{
    public class SalienClient
    {
        private HttpClient Client;
        private readonly string Username;
        private readonly string Token;
        private readonly uint AccountID;
        private readonly double SleepSeconds = 20d;

        public SalienClient(string username, string token, uint accountid)
        {
            Client = new HttpClient();

            Client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
            Client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-AU,en-US,en-GB,en");
            Client.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://steamcommunity.com");
            Client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://steamcommunity.com/saliengame/play/");
            Client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/67.0.3396.87 Safari/537.36");

            Client.Timeout = TimeSpan.FromSeconds(15);

            Client.BaseAddress = new Uri("https://community.steam-api.com");

            Username = username;
            Token = token;
            AccountID = accountid;
        }

        public void Start()
        {
            Output("Starting SalienClient...");

            while (true)
            {
                GetUserInfoResponse UserInfo = GetUserInfo();
                if (UserInfo == null)
                {
                    Output(String.Format("Failed Getting UserInfo, retrying in {0} seconds...", SleepSeconds), ConsoleColor.Red);
                    Thread.Sleep(TimeSpan.FromSeconds(SleepSeconds));
                    continue;
                }

                // Leave our current game
                if (UserInfo.Active_Zone_Game != 0)
                {
                    Output("Leaving Active Zone...");
                    EResult LeaveGameResult = LeaveGame(UserInfo.Active_Zone_Game);
                    if (LeaveGameResult == EResult.OK)
                    {
                        Output("Left Active Zone!", ConsoleColor.Green);
                    }
                    else
                    {
                        Output(String.Format("Error Leaving Active Zone: {0}, retrying in {1} seconds...", LeaveGameResult.ToString(), SleepSeconds), ConsoleColor.Red);
                        Thread.Sleep(TimeSpan.FromSeconds(SleepSeconds));
                        continue;
                    }
                }

                // Leave our current planet
                if (UserInfo.Active_Planet != 0)
                {
                    Output("Leaving Active Planet...");
                    EResult LeavePlanetResult = LeaveGame(UserInfo.Active_Planet);
                    if (LeavePlanetResult == EResult.OK)
                    {
                        Output("Left Active Planet!", ConsoleColor.Green);
                    }
                    else
                    {
                        Output(String.Format("Error Leaving Active Planet: {0}, retrying in {1} seconds...", LeavePlanetResult.ToString(), SleepSeconds), ConsoleColor.Red);
                        Thread.Sleep(TimeSpan.FromSeconds(SleepSeconds));
                        continue;
                    }
                }

                // Search for a new game
                Output("Finding Planet and Zone to Join...");
                (Planet planet, Planet_Zone zone) = FindZone();
                if (zone == null)
                {
                    Output(String.Format("Failed to Find a Zone, retrying in {0} seconds...", SleepSeconds), ConsoleColor.Red);
                    Thread.Sleep(TimeSpan.FromSeconds(SleepSeconds));
                    continue;
                }

                if (zone.Type == 4)
                    Output(String.Format("Found Boss Zone: {0} on Planet: {1} ({2})!", zone.Zone_Position, planet.ID, planet.State.Name), ConsoleColor.Blue);
                else
                    Output(String.Format("Found Zone: {0} with Difficulty: {1} on Planet: {2} ({3})!", zone.Zone_Position, zone.Difficulty, planet.ID, planet.State.Name), ConsoleColor.Green);

                // Join Planet
                Output("Joining Planet...");
                EResult JoinPlanetResult = JoinPlanet(planet.ID);
                if (JoinPlanetResult == EResult.OK)
                {
                    Output("Joined Planet!", ConsoleColor.Green);
                }
                else
                {
                    Output(String.Format("Error Joining Planet: {0}, retrying in {1} seconds...", JoinPlanetResult.ToString(), SleepSeconds), ConsoleColor.Red);
                    Thread.Sleep(TimeSpan.FromSeconds(SleepSeconds));
                    continue;
                }

                while (true)
                {
                    // Join Zone
                    Output("Joining Zone...");

                    // Boss Zone
                    if (zone.Type == 4)
                    {
                        (EResult Result, bool WaitingForPlayers) JoinBossZoneResult = JoinBossZone(zone.Zone_Position);
                        if (JoinBossZoneResult.Result == EResult.OK)
                        {
                            Output("Joined Boss Zone!", ConsoleColor.Blue);
                        }
                        else
                        {
                            Output(String.Format("Error Joining Boss Zone: {0}, retrying in {1} seconds...", JoinBossZoneResult.Result.ToString(), SleepSeconds), ConsoleColor.Red);
                            Thread.Sleep(TimeSpan.FromSeconds(SleepSeconds));
                            break;
                        }

                        // Report Score
                        int Heal = 0;
                        int Fails = 0;
                        int PreviousXPGained = 0;
                        bool WaitingForPlayers = JoinBossZoneResult.WaitingForPlayers;
                        while (true)
                        {
                            (EResult Result, ReportBossDamageResponse Response) = ReportBossDamage(WaitingForPlayers, (Heal == 23));
                            if (Result == EResult.OK)
                            {
                                if (Response.Boss_Status == null)
                                {
                                    Output(String.Format("Waiting... Sleeping for {0} seconds...", SleepSeconds), ConsoleColor.Blue);
                                    Thread.Sleep(TimeSpan.FromSeconds(SleepSeconds));
                                    continue;
                                }
                                else
                                {
                                    WaitingForPlayers = Response.Waiting_For_Players;
                                }

                                if (WaitingForPlayers)
                                {
                                    Output(String.Format("Waiting for players... Sleeping for {0}", SleepSeconds), ConsoleColor.Blue);
                                    Thread.Sleep(TimeSpan.FromSeconds(SleepSeconds));
                                    continue;
                                }

                                string output = String.Format("Boss HP: {0} / {1}\t Lasers: {2}, Heals: {3}\t",
                                    Response.Boss_Status.Boss_HP, Response.Boss_Status.Boss_Max_HP,
                                    Response.Num_Laser_Uses, Response.Num_Team_Heals);

                                if (Response.Game_Over)
                                    output = "GAME OVER";

                                if (AccountID != 0)
                                {
                                    Boss_Player Player = Response.Boss_Status.Boss_Players.First(x => x.AccountID == AccountID);
                                    if (!Response.Game_Over)
                                    {
                                        output += String.Format("HP: {0} / {1}, Level: {2}, Score: {3} / {4}, XP Earned: {5}",
                                            Player.HP, Player.Max_HP, Player.New_Level, Player.Score_On_Join + Player.XP_Earned, Player.Next_Level_Score, Player.XP_Earned);

                                        PreviousXPGained = Player.XP_Earned;
                                    }
                                    else
                                    {
                                        output += String.Format(", XP Gained: {0}, Bonus XP: {1}, Total XP: {2}",
                                            PreviousXPGained, Player.XP_Earned - PreviousXPGained, Player.XP_Earned);
                                    }
                                }
                                Output(output, ConsoleColor.Blue);

                                if (Response.Game_Over)
                                    break;
                            }
                            else if (Result == EResult.InvalidState)
                            {
                                break;
                            }
                            else
                            {
                                Output(String.Format("Error Reporting Boss Damage: {0}, retrying in {1} seconds...", Result.ToString(), SleepSeconds), ConsoleColor.Red);
                                Fails++;
                                Thread.Sleep(TimeSpan.FromSeconds(SleepSeconds));
                            }

                            if (Fails > 5)
                                break;

                            Heal++;

                            if (Heal > 23)
                                Heal = 0;

                            Thread.Sleep(TimeSpan.FromSeconds(5));
                        }
                        break;
                    }
                    // Normal Zone
                    else
                    {
                        EResult JoinZoneResult = JoinZone(zone.Zone_Position);
                        if (JoinZoneResult == EResult.OK)
                        {
                            Output("Joined Zone!", ConsoleColor.Green);
                        }
                        else if (JoinZoneResult == EResult.Expired)
                        {
                            Output("The Zone we tried joining has been captured...", ConsoleColor.Yellow);
                            break;
                        }
                        else
                        {
                            Output(String.Format("Error Joining Zone: {0}, retrying in {1} seconds...", JoinZoneResult.ToString(), SleepSeconds), ConsoleColor.Red);
                            Thread.Sleep(TimeSpan.FromSeconds(SleepSeconds));
                            break;
                        }


                        // Wait (and check if a better zone was found)
                        Output("Sleeping for 110 seconds...");
                        for (int i = 1; i < 110; i++)
                        {
                            Thread.Sleep(TimeSpan.FromSeconds(1));
                            if (Program.NewZone != null)
                            {
                                if (zone.Difficulty < Program.NewZone.Difficulty || Program.NewZone.Type == 4)
                                    break;
                            }
                        }
                        if (Program.NewZone != null)
                        {
                            if (zone.Difficulty < Program.NewZone.Difficulty || Program.NewZone.Type == 4)
                                break;
                        }

                        // Report Score
                        (EResult Result, ReportScoreResponse Response) = ReportScore(300 << zone.Difficulty);
                        if (Result == EResult.OK)
                        {
                            Output(String.Format("Finished Zone for {0} XP... Current Level: {1}, Current Score: {2}, Next Level Score: {3}",
                                300 << zone.Difficulty, Response.New_Level, Response.New_Score, Response.Next_Level_Score), ConsoleColor.Magenta);
                        }
                        else if (Result == EResult.NoMatch)
                        {
                            Output("The Zone we just finished was captured before we could report our score...", ConsoleColor.Yellow);
                            break;
                        }
                        else
                        {
                            Output(String.Format("Error Reporting Score: {0}, retrying in {1} seconds...", Response.ToString(), SleepSeconds), ConsoleColor.Red);
                            Thread.Sleep(TimeSpan.FromSeconds(SleepSeconds));
                            break;
                        }
                    }
                }
            }
        }

        public void Output(string output, ConsoleColor color = ConsoleColor.White)
        {
            Console.ForegroundColor = color;
            Console.WriteLine(String.Format("[{0}/SalienClient] {1}", Username, output));
        }

        private (Dictionary<string, string> Headers, string Data) Request(string URI, string Method, Dictionary<string, string> Data = null)
        {
            try
            {
                if (Method == "POST")
                {
                    var Response = Client.PostAsync(URI, new FormUrlEncodedContent(Data));
                    return (Response.Result.Headers.ToDictionary(x => x.Key, x => x.Value.FirstOrDefault()), Response.Result.Content.ReadAsStringAsync().Result);
                }
                else
                {
                    if (Data != null)
                    {
                        NameValueCollection Collection = HttpUtility.ParseQueryString(String.Empty);
                        foreach (KeyValuePair<string, string> KeyValue in Data)
                            Collection[KeyValue.Key] = KeyValue.Value;

                        URI = URI + "?" + Collection.ToString();
                    }
                    var Response = Client.GetAsync(URI);
                    return (Response.Result.Headers.ToDictionary(x => x.Key, x => x.Value.FirstOrDefault()), Response.Result.Content.ReadAsStringAsync().Result);
                }
            }
            catch (Exception ex)
            {
                Output(String.Format("HttpClient Request Error: {0}", ex.Message), ConsoleColor.Red);
                return (null, null);
            }
        }

        private GetUserInfoResponse GetUserInfo()
        {
            Dictionary<string, string> Data = new Dictionary<string, string>();
            Data.Add("access_token", Token);
            (Dictionary<string, string> Headers, string Response) = Request("/ITerritoryControlMinigameService/GetPlayerInfo/v0001/", "POST", Data);
            if (Response == null)
                return null;
            return JObject.Parse(Response).SelectToken("response", false).ToObject<GetUserInfoResponse>();
        }

        private GetPlanetsResponse GetPlanets(bool Active_Only = true)
        {
            Dictionary<string, string> Data = new Dictionary<string, string>();
            Data.Add("active_only", Convert.ToByte(Active_Only).ToString());
            Data.Add("language", "english");
            (Dictionary<string, string> Headers, string Response) = Request("/ITerritoryControlMinigameService/GetPlanets/v0001/", "GET", Data);
            if (Response == null)
                return null;
            return JObject.Parse(Response).SelectToken("response", false).ToObject<GetPlanetsResponse>();
        }

        private GetPlanetsResponse GetPlanet(short PlanetID)
        {
            Dictionary<string, string> Data = new Dictionary<string, string>();
            Data.Add("id", PlanetID.ToString());
            (Dictionary<string, string> Headers, string Response) = Request("/ITerritoryControlMinigameService/GetPlanet/v0001/", "GET", Data);
            if (Response == null)
                return null;
            return JObject.Parse(Response).SelectToken("response", false).ToObject<GetPlanetsResponse>();
        }

        private (Planet planet, Planet_Zone zone) GetZone(short PlanetID, short Zone_Position)
        {
            Planet planet = GetPlanet(PlanetID).Planets[0];
            if (planet == null)
                return (null, null);
            return (planet, planet.Zones.First(x => x.Zone_Position == Zone_Position));
        }

        public (Planet planet, Planet_Zone zone) FindZone()
        {
            Planet[] Planets = GetPlanets().Planets;
            if (Planets == null)
                return (null, null);

            for (int i = 0; i < Planets.Count(); i++)
                Planets[i].Zones = GetPlanet(Planets[i].ID).Planets[0].Zones.Where(x => !x.Captured && x.Capture_Progress != 0).OrderByDescending(y => y.Difficulty).ToArray();

            if (Planets.Count(x => x.State.Boss_Zone_Position != 0 && x.Zones.Count(y => y.Boss_Active) > 0) > 0)
            {
                Planet p = Planets.First(x => x.State.Boss_Zone_Position != 0 && x.Zones.Count(y => y.Boss_Active) > 0);
                return (p, p.Zones.First(x => x.Boss_Active));
            }
            if (Planets.Count(x => x.Zones[0].Difficulty == 3) > 0)
            {
                Planet p = Planets.First(x => x.Zones[0].Difficulty == 3);
                return (p, p.Zones[0]);
            }
            else if (Planets.Count(x => x.Zones[0].Difficulty == 2) > 0)
            {
                Planet p = Planets.First(x => x.Zones[0].Difficulty == 2);
                return (p, p.Zones[0]);
            }
            else if (Planets.Count(x => x.Zones[0].Difficulty == 1) > 0)
            {
                Planet p = Planets.First(x => x.Zones[0].Difficulty == 1);
                return (p, p.Zones[0]);
            }
            else
            {
                return (null, null);
            }
        }

        private EResult JoinPlanet(short Planet)
        {
            Dictionary<string, string> Data = new Dictionary<string, string>();
            Data.Add("access_token", Token);
            Data.Add("id", Planet.ToString());
            (Dictionary<string, string> Headers, string Response) = Request("/ITerritoryControlMinigameService/JoinPlanet/v0001/", "POST", Data);
            if (Response == null)
                return EResult.BadResponse;
            return (EResult)Convert.ToInt16(Headers["X-eresult"]);
        }

        private EResult JoinZone(short Zone)
        {
            Dictionary<string, string> Data = new Dictionary<string, string>();
            Data.Add("access_token", Token);
            Data.Add("zone_position", Zone.ToString());
            (Dictionary<string, string> Headers, string Response) = Request("/ITerritoryControlMinigameService/JoinZone/v0001/", "POST", Data);
            // The normal response from this can be parsed from json into a 'Planet_Zone',
            // since we don't do anything with the object, and if the request fails the json will be null,
            // it's best just to return an EResult
            if (Response == null)
                return EResult.BadResponse;
            return (EResult)Convert.ToInt16(Headers["X-eresult"]);
            //return JObject.Parse(Response).SelectToken("response", false).SelectToken("zone_info", false).ToObject<Planet_Zone>();
        }

        private (EResult Result, bool WaitingForPlayers) JoinBossZone(short Zone)
        {
            Dictionary<string, string> Data = new Dictionary<string, string>();
            Data.Add("zone_position", Zone.ToString());
            Data.Add("access_token", Token);
            (Dictionary<string, string> Headers, string Response) = Request("/ITerritoryControlMinigameService/JoinBossZone/v0001/", "POST", Data);
            if (Response == null)
                return (EResult.BadResponse, true);
            return ((EResult)Convert.ToInt16(Headers["X-eresult"]),
                Convert.ToBoolean(JObject.Parse(Response).SelectToken("response", false).SelectToken("waiting_for_players").ToString()));
        }

        private EResult LeaveGame(int GameID)
        {
            Dictionary<string, string> Data = new Dictionary<string, string>();
            Data.Add("access_token", Token);
            Data.Add("gameid", GameID.ToString());
            (Dictionary<string, string> Headers, string Response) = Request("/IMiniGameService/LeaveGame/v0001/", "POST", Data);
            if (Response == null)
                return EResult.BadResponse;
            return (EResult)Convert.ToInt16(Headers["X-eresult"]);
        }

        private (EResult Result, ReportScoreResponse Response) ReportScore(int Score)
        {
            Dictionary<string, string> Data = new Dictionary<string, string>();
            Data.Add("access_token", Token);
            Data.Add("score", Score.ToString());
            Data.Add("language", "english");
            (Dictionary<string, string> Headers, string Response) = Request("/ITerritoryControlMinigameService/ReportScore/v0001/", "POST", Data);
            if (Response == null)
                return (EResult.BadResponse, null);
            return ((EResult)Convert.ToInt16(Headers["X-eresult"]), JObject.Parse(Response).SelectToken("response", false).ToObject<ReportScoreResponse>());
        }

        private (EResult Result, ReportBossDamageResponse Response) ReportBossDamage(bool WaitingForPlayers, bool Heal = false)
        {
            Dictionary<string, string> Data = new Dictionary<string, string>();
            Data.Add("access_token", Token);
            Data.Add("use_heal_ability", Convert.ToByte(Heal).ToString());
            // Max Damage = 150
            Data.Add("damage_to_boss", Convert.ToByte(!WaitingForPlayers).ToString());
            Data.Add("damage_taken", "0");
            (Dictionary<string, string> Headers, string Response) = Request("/ITerritoryControlMinigameService/ReportBossDamage/v0001/", "POST", Data);
            if (Response == null)
                return (EResult.BadResponse, null);
            return ((EResult)Convert.ToInt16(Headers["X-eresult"]), JObject.Parse(Response).SelectToken("response", false).ToObject<ReportBossDamageResponse>());
        }


        public class GetTokenResponse
        {
            [JsonProperty("persona_name")]
            public string Persona_Name { get; set; }

            [JsonProperty("steamid")]
            public ulong SteamID { get; set; }

            [JsonProperty("success")]
            public EResult Success { get; set; }

            [JsonProperty("token")]
            public string Token { get; set; }

            [JsonProperty("webapi_host")]
            public string WebAPI_Host { get; set; }

            [JsonProperty("webapi_host_secure")]
            public string WebAPI_Host_Secure { get; set; }
        }

        private class GetUserInfoResponse
        {
            [JsonProperty("active_planet")]
            public short Active_Planet { get; set; }

            [JsonProperty("time_on_planet")]
            public int Time_On_Planet { get; set; }

            [JsonProperty("active_zone_game")]
            public int Active_Zone_Game { get; set; }

            [JsonProperty("active_zone_position")]
            public short Active_Zone_Position { get; set; }

            [JsonProperty("time_in_zone")]
            public int Time_In_Zone { get; set; }

            [JsonProperty("clan_info")]
            public Planet_Clan_Info Clan_Info { get; set; }

            [JsonProperty("score")]
            public int Score { get; set; }

            [JsonProperty("level")]
            public short Level { get; set; }

            [JsonProperty("next_level_score")]
            public int Next_Level_Score { get; set; }
        }

        private class GetPlanetsResponse
        {
            [JsonProperty("planets")]
            public Planet[] Planets { get; set; }
        }

        public class Planet
        {
            [JsonProperty("id")]
            public short ID { get; set; }

            [JsonProperty("state")]
            public Planet_State State { get; set; }

            [JsonProperty("giveaway_apps")]
            public int[] Giveaway_Apps { get; set; }

            [JsonProperty("top_clans")]
            public Planet_Clan[] Top_Clans { get; set; }

            [JsonProperty("zones")]
            public Planet_Zone[] Zones { get; set; }
        }

        public class Planet_State
        {
            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("image_filename")]
            public string Image_FileName { get; set; }

            [JsonProperty("map_filename")]
            public string Map_FileName { get; set; }

            [JsonProperty("cloud_filename")]
            public string Cloud_FileName { get; set; }

            [JsonProperty("land_filename")]
            public string Land_FileName { get; set; }

            [JsonProperty("difficulty")]
            public short Difficulty { get; set; }

            [JsonProperty("giveaway_id")]
            public ulong Giveaway_ID { get; set; }

            [JsonProperty("active")]
            public bool Active { get; set; }

            [JsonProperty("activation_time")]
            public ulong Activation_Time { get; set; }

            [JsonProperty("position")]
            public short Position { get; set; }

            [JsonProperty("captured")]
            public bool Captured { get; set; }

            [JsonProperty("capture_progress")]
            public double Capture_Progress { get; set; }

            [JsonProperty("total_joins")]
            public int Total_Joins { get; set; }

            [JsonProperty("current_players")]
            public int Current_Players { get; set; }

            [JsonProperty("priority")]
            public short Priority { get; set; }

            [JsonProperty("tag_ids")]
            public string TagIDs { get; set; }

            [JsonProperty("boss_zone_position")]
            public short Boss_Zone_Position { get; set; }
        }

        public class Planet_Clan
        {
            [JsonProperty("clan_info")]
            public Planet_Clan_Info Clan_Info { get; set; }

            [JsonProperty("num_zones_controled")]
            public short Num_Zones_Controlled { get; set; }
        }

        public class Planet_Clan_Info
        {
            [JsonProperty("accountid")]
            public uint AccountID { get; set; }

            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("avatar")]
            public string Avatar { get; set; }

            [JsonProperty("url")]
            public string URL { get; set; }
        }

        public class Planet_Zone
        {
            [JsonProperty("zone_position")]
            public short Zone_Position { get; set; }

            [JsonProperty("leader")]
            public Planet_Clan_Info Leader { get; set; }

            [JsonProperty("type")]
            public short Type { get; set; }

            [JsonProperty("gameid")]
            public int GameID { get; set; }

            [JsonProperty("difficulty")]
            public short Difficulty { get; set; }

            [JsonProperty("captured")]
            public bool Captured { get; set; }

            [JsonProperty("capture_progress")]
            public double Capture_Progress { get; set; }

            [JsonProperty("top_clans")]
            public Planet_Clan_Info[] Top_Clans { get; set; }

            [JsonProperty("boss_active")]
            public bool Boss_Active { get; set; }
        }

        private class ReportScoreResponse
        {
            [JsonProperty("old_score")]
            public int Old_Score { get; set; }

            [JsonProperty("old_level")]
            public short Old_Level { get; set; }

            [JsonProperty("new_score")]
            public int New_Score { get; set; }

            [JsonProperty("new_level")]
            public short New_Level { get; set; }

            [JsonProperty("next_level_score")]
            public int Next_Level_Score { get; set; }
        }

        private class ReportBossDamageResponse
        {
            [JsonProperty("boss_status")]
            public Boss_Status Boss_Status { get; set; }

            [JsonProperty("waiting_for_players")]
            public bool Waiting_For_Players { get; set; }

            [JsonProperty("game_over")]
            public bool Game_Over { get; set; }

            [JsonProperty("num_laser_uses")]
            public short Num_Laser_Uses { get; set; }

            [JsonProperty("num_team_heals")]
            public short Num_Team_Heals { get; set; }
        }

        private class Boss_Status
        {
            [JsonProperty("boss_hp")]
            public long Boss_HP { get; set; }

            [JsonProperty("boss_max_hp")]
            public long Boss_Max_HP { get; set; }

            [JsonProperty("boss_players")]
            public Boss_Player[] Boss_Players { get; set; }
        }

        private class Boss_Player
        {
            [JsonProperty("accountid")]
            public uint AccountID { get; set; }

            [JsonProperty("clan_info")]
            public Planet_Clan_Info Clan_Info { get; set; }

            [JsonProperty("time_joined")]
            public ulong Time_Joined { get; set; }

            [JsonProperty("time_last_seen")]
            public ulong Time_Last_Seen { get; set; }

            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("hp")]
            public int HP { get; set; }

            [JsonProperty("max_hp")]
            public int Max_HP { get; set; }

            [JsonProperty("salien")]
            public Boss_Player_Salien Salien { get; set; }

            [JsonProperty("score_on_join")]
            public int Score_On_Join { get; set; }

            [JsonProperty("level_on_join")]
            public short Level_On_Join { get; set; }

            [JsonProperty("xp_earned")]
            public int XP_Earned { get; set; }

            [JsonProperty("new_level")]
            public short New_Level { get; set; }

            [JsonProperty("next_level_score")]
            public int Next_Level_Score { get; set; }
        }

        private class Boss_Player_Salien
        {
            [JsonProperty("body_type")]
            public short Body_Type { get; set; }

            [JsonProperty("mouth")]
            public short Mouth { get; set; }

            [JsonProperty("eyes")]
            public short Eyes { get; set; }

            [JsonProperty("arms")]
            public short Arms { get; set; }

            [JsonProperty("legs")]
            public short Legs { get; set; }

            [JsonProperty("hat_itemid")]
            public ulong Hat_ItemID { get; set; }

            [JsonProperty("hat_imageid")]
            public string Hat_ImageID { get; set; }

            [JsonProperty("shirt_itemid")]
            public ulong Short_ItemID { get; set; }

            [JsonProperty("shirt_imageid")]
            public string Shirt_ImageID { get; set; }
        }

        public enum EResult
        {
            Invalid = 0,
            OK = 1,
            Fail = 2,
            NoConnection = 3,
            InvalidPassword = 5,
            LoggedInElsewhere = 6,
            InvalidProtocolVer = 7,
            InvalidParam = 8,
            FileNotFound = 9,
            Busy = 10,
            InvalidState = 11,
            InvalidName = 12,
            InvalidEmail = 13,
            DuplicateName = 14,
            AccessDenied = 15,
            Timeout = 16,
            Banned = 17,
            AccountNotFound = 18,
            InvalidSteamID = 19,
            ServiceUnavailable = 20,
            NotLoggedOn = 21,
            Pending = 22,
            EncryptionFailure = 23,
            InsufficientPrivilege = 24,
            LimitExceeded = 25,
            Revoked = 26,
            Expired = 27,
            AlreadyRedeemed = 28,
            DuplicateRequest = 29,
            AlreadyOwned = 30,
            IPNotFound = 31,
            PersistFailed = 32,
            LockingFailed = 33,
            LogonSessionReplaced = 34,
            ConnectFailed = 35,
            HandshakeFailed = 36,
            IOFailure = 37,
            RemoteDisconnect = 38,
            ShoppingCartNotFound = 39,
            Blocked = 40,
            Ignored = 41,
            NoMatch = 42,
            AccountDisabled = 43,
            ServiceReadOnly = 44,
            AccountNotFeatured = 45,
            AdministratorOK = 46,
            ContentVersion = 47,
            TryAnotherCM = 48,
            PasswordRequiredToKickSession = 49,
            AlreadyLoggedInElsewhere = 50,
            Suspended = 51,
            Cancelled = 52,
            DataCorruption = 53,
            DiskFull = 54,
            RemoteCallFailed = 55,
            PasswordUnset = 56,
            ExternalAccountUnlinked = 57,
            PSNTicketInvalid = 58,
            ExternalAccountAlreadyLinked = 59,
            RemoteFileConflict = 60,
            IllegalPassword = 61,
            SameAsPreviousValue = 62,
            AccountLogonDenied = 63,
            CannotUseOldPassword = 64,
            InvalidLoginAuthCode = 65,
            AccountLogonDeniedNoMail = 66,
            HardwareNotCapableOfIPT = 67,
            IPTInitError = 68,
            ParentalControlRestricted = 69,
            FacebookQueryError = 70,
            ExpiredLoginAuthCode = 71,
            IPLoginRestrictionFailed = 72,
            AccountLockedDown = 73,
            AccountLogonDeniedVerifiedEmailRequired = 74,
            NoMatchingURL = 75,
            BadResponse = 76,
            RequirePasswordReEntry = 77,
            ValueOutOfRange = 78,
            UnexpectedError = 79,
            Disabled = 80,
            InvalidCEGSubmission = 81,
            RestrictedDevice = 82,
            RegionLocked = 83,
            RateLimitExceeded = 84,
            AccountLoginDeniedNeedTwoFactor = 85,
            ItemDeleted = 86,
            AccountLoginDeniedThrottle = 87,
            TwoFactorCodeMismatch = 88,
            TwoFactorActivationCodeMismatch = 89,
            AccountAssociatedToMultiplePartners = 90,
            NotModified = 91,
            NoMobileDevice = 92,
            TimeNotSynced = 93,
            SMSCodeFailed = 94,
            AccountLimitExceeded = 95,
            AccountActivityLimitExceeded = 96,
            PhoneActivityLimitExceeded = 97,
            RefundToWallet = 98,
            EmailSendFailure = 99,
            NotSettled = 100,
            NeedCaptcha = 101,
            GSLTDenied = 102,
            GSOwnerDenied = 103,
            InvalidItemType = 104,
            IPBanned = 105,
            GSLTExpired = 106,
            InsufficientFunds = 107,
            TooManyPending = 108,
            NoSiteLicensesFound = 109,
            WGNetworkSendExceeded = 110,
            AccountNotFriends = 111,
            LimitedUserAccount = 112,
        }
    }
}
