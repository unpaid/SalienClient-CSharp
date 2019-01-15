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
        private readonly short[] Scores = { 600, 1200, 2400 };
        private readonly double SleepSeconds = 20d;

        public SalienClient(string username, string token)
        {
            Client = new HttpClient();

            Client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, */*");
            Client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-AU,en-US,en-GB,en");
            Client.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://steamcommunity.com");
            Client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://steamcommunity.com/saliengame/play/");
            Client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/67.0.3396.87 Safari/537.36");

            Client.Timeout = TimeSpan.FromSeconds(15);

            Client.BaseAddress = new Uri("https://community.steam-api.com");

            Username = username;
            Token = token;
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
                        Output("Left Active Zone!", ConsoleColor.Green);
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
                        Output("Left Active Planet!", ConsoleColor.Green);
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
                Output(String.Format("Found Zone: {0} with Difficulty: {1} on Planet: {2} ({3})!", zone.Zone_Position, zone.Difficulty, planet.ID, planet.State.Name), ConsoleColor.Green);

                // Join Planet
                Output("Joining Planet...");
                EResult JoinPlanetResult = JoinPlanet(planet.ID);
                if (JoinPlanetResult == EResult.OK)
                    Output("Joined Planet!", ConsoleColor.Green);
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
                    EResult JoinZoneResult = JoinZone(zone.Zone_Position);
                    if (JoinZoneResult == EResult.OK)
                        Output("Joined Zone!", ConsoleColor.Green);
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

                    // Wait
                    Output("Sleeping for 110 seconds...");
                    Thread.Sleep(TimeSpan.FromSeconds(110));

                    // Report Score
                    (EResult Result, ReportScoreResponse Response) = ReportScore(Scores[zone.Difficulty - 1]);
                    if (Result == EResult.OK)
                    {
                        Output(String.Format("Finished Zone for {0} XP... Current Level: {1}, Current Score: {2}, Next Level Score: {3}",
                            Scores[zone.Difficulty - 1], Response.New_Level, Response.New_Score, Response.Next_Level_Score), ConsoleColor.Magenta);
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

        private (Planet planet, Planet_Zone zone) FindZone()
        {
            Planet[] Planets = GetPlanets().Planets;
            if (Planets == null)
                return (null, null);

            for (int i = 0; i < Planets.Count(); i++)
                Planets[i].Zones = GetPlanet(Planets[i].ID).Planets[0].Zones.Where(x => !x.Captured).OrderByDescending(y => y.Difficulty).ToArray();

            int Highest = 0;
            Planet planet = null;
            Planet_Zone planet_zone = null;
            foreach (Planet p in Planets)
            {
                if (p.Zones[0].Difficulty > Highest)
                {
                    planet = p;
                    planet_zone = p.Zones[0];
                    Highest = planet_zone.Difficulty;
                }
            }
            return (planet, planet_zone);
        }

        private (Planet planet, Planet_Zone zone) GetZone(short PlanetID, short Zone_Position)
        {
            Planet planet = GetPlanet(PlanetID).Planets[0];
            if (planet == null)
                return (null, null);
            return (planet, planet.Zones.First(x => x.Zone_Position == Zone_Position));
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

        private EResult LeaveGame(short GameID)
        {
            Dictionary<string, string> Data = new Dictionary<string, string>();
            Data.Add("access_token", Token);
            Data.Add("gameid", GameID.ToString());
            (Dictionary<string, string> Headers, string Response) = Request("/IMiniGameService/LeaveGame/v0001/", "POST", Data);
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

        private (EResult Result, ReportScoreResponse Response) ReportScore(short Score)
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
            public short Active_Zone_Game { get; set; }

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

        private class Planet
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

        private class Planet_State
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
        }

        private class Planet_Clan
        {
            [JsonProperty("clan_info")]
            public Planet_Clan_Info Clan_Info { get; set; }

            [JsonProperty("num_zones_controled")]
            public short Num_Zones_Controlled { get; set; }
        }

        private class Planet_Clan_Info
        {
            [JsonProperty("accountid")]
            public ulong AccountID { get; set; }

            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("avatar")]
            public string Avatar { get; set; }

            [JsonProperty("url")]
            public string URL { get; set; }
        }

        private class Planet_Zone
        {
            [JsonProperty("zone_position")]
            public short Zone_Position { get; set; }

            [JsonProperty("leader")]
            public Planet_Clan_Info Leader { get; set; }

            [JsonProperty("type")]
            public short Type { get; set; }

            [JsonProperty("gameid")]
            public short GameID { get; set; }

            [JsonProperty("difficulty")]
            public short Difficulty { get; set; }

            [JsonProperty("captured")]
            public bool Captured { get; set; }

            [JsonProperty("capture_progress")]
            public double Caputure_Progress { get; set; }

            [JsonProperty("top_clans")]
            public Planet_Clan_Info[] Top_Clans { get; set; }
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
