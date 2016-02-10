using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using Akka;
using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.Event;
using Akka.TestKit;
using Akka.TestKit.Xunit2;
using Stripe;

namespace ActorSupervisionDeepDive {
    public class ActorTests : TestKit {
        public ActorTests(ITestOutputHelper output = null) : base(output: output) {

        }

        [Fact]
        public void OrderProcessorActor_end_to_end() {
            var message = new PlaceOrder(12345, 10, 25, 5000);
            var orderProcessorActor = ActorOfAsTestActorRef(
                        () => new OrderProcessorActor(), TestActor);
            orderProcessorActor.Tell(message);

            Assert.True(ExpectMsg<AccountCharged>().Success);
        }
        [Fact]
        public void OrderProcessorActor_end_to_end_bad_data() {
            var message = new PlaceOrder(12345, 10, 25, -5000);
            var orderProcessorActor = ActorOfAsTestActorRef(
                        () => new OrderProcessorActor(), TestActor);
            orderProcessorActor.Tell(message);

            Assert.False(ExpectMsg<AccountCharged>().Success);
        }
        [Fact]
        public void OrderProcessorActor_handles_placeOrderCommand_creates_orderActor() {
            var message = new PlaceOrder(12345, 10, 25, 5000);
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
            var message = new PlaceOrder(12345, 10, 25, 5000);
            var orderProcessorActor = CreateTestProbe();
            var orderActor = ActorOfAsTestActorRef<OrderActor>(Props.Create(() => new OrderActor(12345)), orderProcessorActor);
            orderActor.Tell(message);

            orderProcessorActor.ExpectMsg<OrderPlaced>();
        }

        [Fact]
        public void AccountActor_handles_chargeCreditCardCommand() {
            var message = new ChargeCreditCard(5000);
            var orderProcessorActor = CreateTestProbe();
            var accountActor = ActorOfAsTestActorRef<AccountActor>(Props.Create(() => new AccountActor(12345)), orderProcessorActor);
            accountActor.Tell(message);

            Assert.True(orderProcessorActor.ExpectMsg<AccountCharged>().Success);
        }

        [Fact]
        public void AccountActor_negative_moneys_throws_exception_from_stripeGateway() {
            var message = new ChargeCreditCard(-5000);
            var accountActor = ActorOf(Props.Create(() => new AccountActor(12345)));

            EventFilter.Exception<StripeException>()
                .ExpectOne(() => accountActor.Tell(message));
        }

        [Fact]
        public void AccountActor_gets_stopped_with_badData() {
            var message = new ChargeCreditCard(-5000);
            var accountActor = ActorOf(Props.Create(() => new AccountActor(12345)));

            EventFilter.Warning("AccountActor stopped!")
                .ExpectOne(() => accountActor.Tell(message));
        }

        [Fact]
        public void AccountActor_gets_stopping_directive() {
            var message = new ChargeCreditCard(-5000);
            var accountActor = ActorOf(
                Props.Create(() => new AccountActor(12345),
                SupervisorStrategy.DefaultStrategy));

            EventFilter.Warning("AccountActor stopped!")
                .ExpectOne(() => accountActor.Tell(message));
        }

        [Fact]
        public void OrderProcessorActor_with_resume_directive_doesnt_stop_accountActor() {
            var message = new PlaceOrder(12345, 10, 25, -5000);
            var orderProcessorActor = ActorOfAsTestActorRef(
                () => new OrderProcessorActor(), TestActor);

            EventFilter.Warning("AccountActor stopped!")
                .Expect(0, () => orderProcessorActor.Tell(message));
        }

        [Fact]
        public void OrderProcessorActor_applies_custom_supervisor_strategy() {
            var message = new PlaceOrder(12345, 10, 25, -5000);
            var orderProcessorActor = ActorOfAsTestActorRef(
                () => new OrderProcessorActor(), TestActor);

            EventFilter.Warning("AccountActor stopped!")
                .Expect(0, () => orderProcessorActor.Tell(message));
        }

        [Fact]
        public void OrderProcessorActor_logs_warning_about_bad_charge() {
            var message = new PlaceOrder(12345, 10, 25, -5000);
            var orderProcessorActor = ActorOfAsTestActorRef(
                        () => new OrderProcessorActor(), TestActor);
            
            EventFilter.Error("Error! Account not charged!")
                .ExpectOne(() => orderProcessorActor.Tell(message));
        }
    }
}