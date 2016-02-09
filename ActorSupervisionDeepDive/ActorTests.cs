using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using Akka;
using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.TestKit;
using Akka.TestKit.Xunit2;
using Stripe;

namespace ActorSupervisionDeepDive {
    public class ActorTests : TestKit {
        private readonly ITestOutputHelper _output;
        public ActorTests(ITestOutputHelper output) {
            _output = output;
        }

        [Fact]
        public void OrderProcessorActor_end_to_end() {
            var message = new PlaceOrder(12345, 10, 25, 50.00m);
            var orderProcessorActor = ActorOfAsTestActorRef(
                        () => new OrderProcessorActor(), TestActor);
            orderProcessorActor.Tell(message);
            Assert.True(ExpectMsg<AccountCharged>().Success);
        }
        [Fact]
        public void OrderProcessorActor_end_to_end_bad_data() {
            var message = new PlaceOrder(12345, 10, 25, -50.00m);
            var orderProcessorActor = ActorOfAsTestActorRef(
                        () => new OrderProcessorActor(), TestActor);
            orderProcessorActor.Tell(message);
            EventFilter.Exception<StripeException>()
                .Expect(2, () => orderProcessorActor.Tell(message));
        }
        [Fact]
        public void OrderProcessorActor_handles_placeOrderCommand_creates_orderActor() {
            var message = new PlaceOrder(12345, 10, 25, 50.00m);
            var orderProcessorActor = ActorOfAsTestActorRef(
                        () => new OrderProcessorActor(), "orderProcessor");
            orderProcessorActor.Tell(message);
            var orderChild = ActorSelection("/user/orderProcessor/orderActor*")
                .ResolveOne(TimeSpan.FromSeconds(3))
                .Result;
            Assert.True(orderChild.Path.ToString()
                .StartsWith("akka://test/user/orderProcessor/orderActor"));
        }

        [Fact]
        public void OrderActor_handles_placeOrderCommand() {
            var message = new PlaceOrder(12345, 10, 25, 50.00m);
            var orderProcessorActor = CreateTestProbe();
            var orderActor = ActorOfAsTestActorRef<OrderActor>(Props.Create(() => new OrderActor()), orderProcessorActor);
            orderActor.Tell(message);
            orderProcessorActor.ExpectMsg<OrderPlaced>();
        }

        [Fact]
        public void AccountActor_handles_chargeCreditCardCommand() {
            var message = new ChargeCreditCard(1234.50m);
            var orderProcessorActor = CreateTestProbe();
            var accountActor = ActorOfAsTestActorRef<AccountActor>(Props.Create(() => new AccountActor(12345)), orderProcessorActor);
            accountActor.Tell(message);
            orderProcessorActor.ExpectMsg<AccountCharged>();
        }

        [Fact]
        public void AccountActor_throws_exception_from_stripeGateway() {
            var message = new ChargeCreditCard(-1234.50m);
            var accountActor = Sys.ActorOf(Props.Create(() => new AccountActor(12345)));

            EventFilter.Exception<StripeException>()
                .ExpectOne(() => accountActor.Tell(message));
        }

        [Fact]
        public void AccountActor_gets_supervised() {
            var message = new PlaceOrder(12345, 10, 25, -50.00m);
            var orderProcessorActor = Sys.ActorOf(Props.Create<OrderProcessorActor>(() => new OrderProcessorActor()));
            orderProcessorActor.Tell(message);
        }
    }
}