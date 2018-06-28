using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace SalienClientManager
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.Title = "SalienClientManager";
            if (!File.Exists("tokens.txt"))
            {
                Console.WriteLine("tokens.txt does not exist!");
                Console.ReadKey();
                Environment.Exit(1);
            }
            List<Tuple<string, string>> Tokens = File.ReadAllLines("tokens.txt").Select(x => new Tuple<string, string>(x.Split(':')[0], x.Split(':')[1])).ToList();
            foreach (Tuple<string, string> Token in Tokens)
            {
                SalienClient Client = new SalienClient(Token.Item1, Token.Item2);
                Thread Thread = new Thread(Client.Start);
                Thread.Start();
                Thread.Sleep(TimeSpan.FromSeconds(5));
            }
        }
    }
}
