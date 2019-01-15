using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace SalienClientManager
{
    public class Program
    {
        public static SalienClient.Planet_Zone NewZone;

        static void Main(string[] args)
        {
            Console.Title = "SalienClientManager";

            List<SalienClient> SalienClients = new List<SalienClient>();

            if (!File.Exists("tokens.txt"))
            {
                Console.WriteLine("tokens.txt does not exist!");
                Console.ReadKey();
                Environment.Exit(1);
            }

            foreach (string Line in File.ReadAllLines("tokens.txt"))
            {
                string[] Split = Line.Split(':');
                string Name = Split[0];
                string Token = Split[1];
                uint AccountID = 0;
                UInt32.TryParse(Split.Last(), out AccountID);
                SalienClient Client = new SalienClient(Name, Token, AccountID);
                SalienClients.Add(Client);
                Thread Thread = new Thread(Client.Start);
                Thread.Start();
                Thread.Sleep(TimeSpan.FromSeconds(5));
            }

            TimeSpan CheckTime = TimeSpan.FromSeconds(Math.Max(1, 60 / SalienClients.Count));
            int index = 0;
            while (true)
            {
                NewZone = SalienClients[index].FindZone().zone;
                if (index > SalienClients.Count - 1)
                    index = 0;
                Thread.Sleep(CheckTime);
            }
        }
    }
}
