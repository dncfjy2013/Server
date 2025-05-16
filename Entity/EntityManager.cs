using Server.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Entity
{
    public class EntityManager
    {
        public static void ProcessNormalCommand(string Message)
        {
            switch (Message)
            {
                case nameof(MessageType.Normal):
                    Console.WriteLine(Message);
                    break;
                case nameof(MessageType.PrintTime): 
                    Console.WriteLine(DateTime.Now); 
                    break;
                case nameof(MessageType.PrintLog):
                    Console.WriteLine(DateTime.Now + " : " + Message);
                    break;
                case nameof(ControlType.None):
                    break;
                default:
                    break;
            }
        }
    }
}
