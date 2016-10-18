using System;
using System.Threading;

namespace Mumble
{
    public class Program
    {
        private static MumbleClient _mc;

        private static void Main(string[] args)
        {
            _mc = new MumbleClient("192.168.2.8", 64738);
            _mc.Connect("olivier", "");

            Thread t = new Thread(Update);
            t.Start();

            while (true)
            {
                string msg = Console.ReadLine();
                _mc.SendTextMessage(msg);
            }
        }

        // This is the Unity Update() routine
        private static void Update()
        {
            while(true)
              _mc.Process();
        }

    }
}