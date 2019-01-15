using System;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Web;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SteamBot
{
    public class SalienClient
    {
        private HttpClient Client;
        private string Token;
        private short[] Scores = { 600, 1200, 2400 };

        public SalienClient(string token)
        {
            Client = new HttpClient();

            Client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, */*;q=0.5");
            Client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-AU,en-US;q=0.9,en-GB;q=0.8,en;q=0.7");
            Client.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://steamcommunity.com");
            Client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://steamcommunity.com/saliengame/play/");
            Client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/67.0.3396.87 Safari/537.36");

            Client.Timeout = TimeSpan.FromSeconds(15);

            Client.BaseAddress = new Uri("https://community.steam-api.com");

            // If you had a cookie aware client to do web requests,
            // you could make token an optional parameter and then use
            // GetToken() to get the token if it's null
            Token = token;
        }

        public void Start()
        {
            Console.WriteLine("Starting SalienClient...");
            GetUserInfoResponse UserInfo = GetUserInfo();
            if (UserInfo.Active_Planet != 0)
            {
                Console.WriteLine("Leaving Active Planet...");
                LeavePlanet(UserInfo.Active_Planet);
            }
            Console.WriteLine("Finding Planet and Zone to Join...");
            Tuple<Planet, Planet_Zone> Zone = FindZone(GetPlanets().Planets);
            if (Zone != null)
            {
                JoinPlanet(Zone.Item1.ID);
                Console.WriteLine("Joined Planet: {0}...", Zone.Item1.ID);
                while (true)
                {
                    try
                    {
                        JoinZone(Zone.Item2.Zone_Position);
                    }
                    catch (Exception ex)
                    {
                        // Could mean the Zone we were farming had been claimed or Steam is struggling
                        // to handle and return our request
                        Console.WriteLine("Error Joining Zone {0}: {1}, retrying in 30 seconds...", Zone.Item2.Zone_Position, ex.Message);
                        Thread.Sleep(TimeSpan.FromSeconds(30));
                        Start();
                    }
                    Console.WriteLine("Joined Zone: {0}...", Zone.Item2.Zone_Position);
                    Console.WriteLine("Sleeping for 110 seconds...");
                    Thread.Sleep(TimeSpan.FromSeconds(110));
                    ReportScoreResponse Response = ReportScore(Scores[Zone.Item2.Difficulty - 1]);
                    Console.WriteLine("Finished Zone for {0} XP... Current Level: {1}, Current Score: {2}, Next Level Score: {3}",
                        Scores[Zone.Item2.Difficulty - 1], Response.New_Level, Response.New_Score, Response.Next_Level_Score);
                }
            }
            else
            {
                Console.WriteLine("Failed to find Zone, retrying in 30 seconds...");
                Thread.Sleep(TimeSpan.FromSeconds(30));
                Start();
            }
        }

        private string Request(string URI, string Method, Dictionary<string, string> Data = null)
        {
            try
            {
                if (Method == "POST")
                    return Client.PostAsync(URI, new FormUrlEncodedContent(Data)).Result.Content.ReadAsStringAsync().Result;
                else
                {
                    if (Data != null)
                    {
                        NameValueCollection Collection = HttpUtility.ParseQueryString(String.Empty);
                        foreach (KeyValuePair<string, string> KeyValue in Data)
                            Collection[KeyValue.Key] = KeyValue.Value;

                        return Client.GetAsync(URI + "?" + Collection.ToString()).Result.Content.ReadAsStringAsync().Result;
                    }
                    return Client.GetAsync(URI).Result.Content.ReadAsStringAsync().Result;
                }
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        private GetTokenResponse GetToken()
        {
            throw new NotImplementedException();

            //return JsonConvert.DeserializeObject<GetTokenResponse>(Client.Request("https://steamcommunity.com/saliengame/gettoken", "GET", null, Referer));
        }

        private GetUserInfoResponse GetUserInfo()
        {
            Dictionary<string, string> Data = new Dictionary<string, string>();
            Data.Add("access_token", Token);
            return JObject.Parse(Request("/ITerritoryControlMinigameService/GetPlayerInfo/v0001/", "POST", Data)).SelectToken("response", false).ToObject<GetUserInfoResponse>();
        }

        private GetPlanetsResponse GetPlanets(bool Active_Only = true)
        {
            Dictionary<string, string> Data = new Dictionary<string, string>();
            Data.Add("active_only", Convert.ToByte(Active_Only).ToString());
            Data.Add("language", "english");
            return JObject.Parse(Request("/ITerritoryControlMinigameService/GetPlanets/v0001/", "GET", Data)).SelectToken("response", false).ToObject<GetPlanetsResponse>();
        }

        private GetPlanetsResponse GetPlanet(short PlanetID)
        {
            Dictionary<string, string> Data = new Dictionary<string, string>();
            Data.Add("id", PlanetID.ToString());
            return JObject.Parse(Request("/ITerritoryControlMinigameService/GetPlanet/v0001/", "GET", Data)).SelectToken("response", false).ToObject<GetPlanetsResponse>();
        }

        private Tuple<Planet, Planet_Zone> FindZone(Planet[] Planets)
        {
            foreach (Planet planet in Planets)
            {
                Console.WriteLine("Searching for Difficulty 3 Zones on {0}...", planet.State.Name);
                GetPlanetsResponse Response = GetPlanet(planet.ID);
                foreach (Planet_Zone Zone in Response.Planets[0].Zones.Where(x => x.Difficulty == 3))
                {
                    if (!Zone.Captured)
                        return new Tuple<Planet, Planet_Zone>(Response.Planets[0], Zone);
                }
            }
            foreach (Planet planet in Planets)
            {
                Console.WriteLine("Searching for Difficulty 2 Zones on {0}...", planet.State.Name);
                GetPlanetsResponse Response = GetPlanet(planet.ID);
                foreach (Planet_Zone Zone in Response.Planets[0].Zones.Where(x => x.Difficulty == 2))
                {
                    if (!Zone.Captured)
                        return new Tuple<Planet, Planet_Zone>(Response.Planets[0], Zone);
                }
            }
            foreach (Planet planet in Planets)
            {
                Console.WriteLine("Searching for Difficulty 1 Zones on {0}...", planet.State.Name);
                GetPlanetsResponse Response = GetPlanet(planet.ID);
                foreach (Planet_Zone Zone in Response.Planets[0].Zones.Where(x => x.Difficulty == 1))
                {
                    if (!Zone.Captured)
                        return new Tuple<Planet, Planet_Zone>(Response.Planets[0], Zone);
                }
            }
            return null;
        }

        private void JoinPlanet(short Planet)
        {
            Dictionary<string, string> Data = new Dictionary<string, string>();
            Data.Add("access_token", Token);
            Data.Add("id", Planet.ToString());
            Request("/ITerritoryControlMinigameService/JoinPlanet/v0001/", "POST", Data);
        }

        private void LeavePlanet(short Active_Planet)
        {
            Dictionary<string, string> Data = new Dictionary<string, string>();
            Data.Add("access_token", Token);
            Data.Add("gameid", Active_Planet.ToString());
            Request("/IMiniGameService/LeaveGame/v0001/", "POST", Data);
        }

        private Planet_Zone JoinZone(short Zone)
        {
            Dictionary<string, string> Data = new Dictionary<string, string>();
            Data.Add("access_token", Token);
            Data.Add("zone_position", Zone.ToString());
            return JObject.Parse(Request("/ITerritoryControlMinigameService/JoinZone/v0001/", "POST", Data)).SelectToken("response", false).SelectToken("zone_info", false).ToObject<Planet_Zone>();
        }

        private ReportScoreResponse ReportScore(short Score)
        {
            Dictionary<string, string> Data = new Dictionary<string, string>();
            Data.Add("access_token", Token);
            Data.Add("score", Score.ToString());
            Data.Add("language", "english");
            return JObject.Parse(Request("/ITerritoryControlMinigameService/ReportScore/v0001/", "POST", Data)).SelectToken("response", false).ToObject<ReportScoreResponse>();
        }


        private class GetTokenResponse
        {
            [JsonProperty("persona_name")]
            public string Persona_Name { get; set; }

            [JsonProperty("steamid")]
            public ulong SteamID { get; set; }

            // If you are not using SteamKit2, you can implement your own
            // EResult enum or just change this to an integer datatype
            [JsonProperty("success")]
            public int Success { get; set; }

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
    }
}
