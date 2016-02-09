using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using ActorSupervisionDeepDive;

namespace ActorSupervisionConsole {
    class Program {
        public static ActorSystem MyActorSystem;
        static void Main(string[] args) {
            Console.WriteLine("Press enter to test ordering");
            while (true) {
                MyActorSystem = ActorSystem.Create("MyActorSystem");
                IActorRef orderProcessorActor = MyActorSystem.ActorOf(Props.Create(() => new OrderProcessorActor()));
                var key = Console.ReadLine();
                if (key == "order") {
                    var goodMessage = new PlaceOrder(12345, 10, 25, 5000);
                    orderProcessorActor.Tell(goodMessage);
                }
                if (key == "badorder") {
                    var badMessage = new PlaceOrder(12345, 10, 25, -5000);
                    orderProcessorActor.Tell(badMessage);
                }
                else if (key == "exit") {
                    break;
                }
            }
        }
    }
}
