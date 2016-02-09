using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Newtonsoft.Json;
using Stripe;
using Polly;

namespace ActorSupervisionDeepDive {

    public class PlaceOrder {
        public int AccountId { get; }
        public int ItemId { get; }
        public int Quantity { get; }
        public decimal ExtPrice { get; }
        public PlaceOrder(int accountId, int itemId, int quantity, decimal extPrice) {
            AccountId = accountId;
            ItemId = itemId;
            Quantity = quantity;
            ExtPrice = extPrice;
        }
    }

    public class OrderPlaced {
        public string OrderId { get; }
        public PlaceOrder OrderInfo { get; set; }
        public OrderPlaced(string orderId, PlaceOrder orderInfo) {
            OrderId = orderId;
            OrderInfo = orderInfo;
        }
    }

    public class ChargeCreditCard {
        public decimal Amount { get; }
        public ChargeCreditCard(decimal amount) {
            Amount = amount;
        }
    }

    class AccountCharged {
        public int Amount { get; }
        public bool Success { get; }

        public AccountCharged(int amount, bool success = false) {
            Amount = amount;
            Success = success;
        }
    }

    public class OrderProcessorActor : ReceiveActor {
        private readonly ILoggingAdapter _logger = Context.GetLogger();
        public OrderProcessorActor() {
            Receive<PlaceOrder>(placeOrder => PlaceOrderHandler(placeOrder));
            Receive<OrderPlaced>(orderPlaced => OrderPlacedHandler(orderPlaced));
            Receive<AccountCharged>(accountCharged => AccountChargedHandler(accountCharged));
        }
        private void PlaceOrderHandler(PlaceOrder placeOrder) {
            var orderActor = Context.ActorOf(
                Props.Create(
                    () => new OrderActor()), "orderActor" + DateTime.Now.Ticks);
            orderActor.Tell(placeOrder);
        }
        private void OrderPlacedHandler(OrderPlaced orderPlaced) {
            var accountActor = Context.ActorOf(
                Props.Create(
                    () => new AccountActor(orderPlaced.OrderInfo.AccountId)), "accountActor" + DateTime.Now.Ticks);
            accountActor.Tell(new ChargeCreditCard(orderPlaced.OrderInfo.ExtPrice));
        }
        private void AccountChargedHandler(AccountCharged accountCharged) {
            if (accountCharged.Success) {
                _logger.Info("Account charged!\n{0}", accountCharged);
                Context.Parent.Tell(accountCharged);
            }
            else {
                _logger.Error("Error! Account not charged!");
                Context.Parent.Tell(accountCharged);
            }
        }

        protected override SupervisorStrategy SupervisorStrategy() {
            return new OneForOneStrategy(
                maxNrOfRetries: 3, 
                withinTimeRange: TimeSpan.FromSeconds(30),
                localOnlyDecider: x => Directive.Restart);
        }
    }

    class OrderActor : ReceiveActor {
        public OrderActor() {
            Receive<PlaceOrder>(placeOrder => PlaceOrderHandler(placeOrder));
        }
        public void PlaceOrderHandler(PlaceOrder placeOrder) {
            Context.Parent.Tell(
                new OrderPlaced(DateTime.Now.Ticks.ToString(), placeOrder));
        }
    }

    public class AccountActor : ReceiveActor {
        public int AccountId { get; }
        private readonly IStripeGateway _stripeGateway;
        private readonly ILoggingAdapter _logger = Context.GetLogger();
        public AccountActor(int accountId) {
            AccountId = accountId;
            Receive<ChargeCreditCard>(
                chargeCreditCard => ChargeCreditCardHandler(chargeCreditCard));
            // TODO: DI
            _stripeGateway = new StripeGateway();
        }
        private void ChargeCreditCardHandler(ChargeCreditCard chargeCreditCard) {

            //var stripeCharge = Policy
            //    .Handle<StripeException>()
            //    .Retry(3)
            //    .Execute(() => _stripeGateway.CreateCharge(chargeCreditCard.Amount));

            var stripeCharge = _stripeGateway.CreateCharge(chargeCreditCard.Amount);

            if (stripeCharge != null) Context.Parent.Tell(new AccountCharged(stripeCharge.Amount, true));
        }

        protected override void PreRestart(Exception reason, object message) {
            _logger.Warning("AccountActor got restarted from message:\n{0}",
                JsonConvert.SerializeObject(message));
            //Self.Tell(message);
            base.PreRestart(reason, message);
        }
    }

    internal interface IStripeGateway {
        StripeCharge CreateCharge(decimal amount);
    }

    public class StripeGateway : IStripeGateway {
        public StripeCharge CreateCharge(decimal amount) {
            if (amount < 0) {
                throw new StripeException(
                    System.Net.HttpStatusCode.OK,
                    new StripeError() { Code = "card_error" },
                    @"Can't charge card a negative value");
            }
            return new StripeCharge() {
                Amount = (int) (amount * 100),
                Captured = true
            };
        }
    }
}
