using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Metadata.W3cXsd2001;
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
        public int ExtPrice { get; }
        public PlaceOrder(int accountId, int itemId, int quantity, int extPrice) {
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
        public int Amount { get; }
        public ChargeCreditCard(int amount) {
            Amount = amount;
        }
    }

    class AccountCharged {
        public ChargeCreditCard ChargeInfo { get; }
        public bool Success { get; }
        public AccountCharged(ChargeCreditCard chargeInfo, bool success) {
            ChargeInfo = chargeInfo;
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
                    () => new OrderActor(
                        (int)DateTime.Now.Ticks)),
                "orderActor" + DateTime.Now.Ticks);
            orderActor.Tell(placeOrder);
        }
        private void OrderPlacedHandler(OrderPlaced orderPlaced) {
            var accountActor = Context.ActorOf(
                Props.Create(
                    () => new AccountActor(
                        orderPlaced.OrderInfo.AccountId)),
                "accountActor" + orderPlaced.OrderInfo.AccountId);
            accountActor.Tell(new ChargeCreditCard(orderPlaced.OrderInfo.ExtPrice));
        }
        private void AccountChargedHandler(AccountCharged accountCharged) {
            if (accountCharged.Success) {
                _logger.Info("Account charged!\n{0}",
                    JsonConvert.SerializeObject(accountCharged));
                // Sends to TestActor (Test) or CustomerActor (Production)
                Context.Parent.Tell(accountCharged);
            }
            else {
                _logger.Error("Error! Account not charged!");
                // Sends to TestActor (Test) or CustomerActor (Production)
                Context.Parent.Tell(accountCharged);
            }
        }

        protected override SupervisorStrategy SupervisorStrategy() {
            return new OneForOneStrategy(
                Decider.From(x => {
                    if (x is StripeException) return Directive.Resume;
                    return Directive.Restart;
                })
            );
        }
    }

    class OrderActor : ReceiveActor {
        public int OrderId { get; }
        public OrderActor(int orderId) {
            OrderId = orderId;
            Receive<PlaceOrder>(placeOrder => PlaceOrderHandler(placeOrder));
        }
        public void PlaceOrderHandler(PlaceOrder placeOrder) {
            Context.Parent.Tell(
                new OrderPlaced(DateTime.Now.Ticks.ToString(), placeOrder));
        }
    }

    public class AccountActor : ReceiveActor {
        public int AccountId { get; }
        private readonly IStripeGateway _stripeGateway = new StripeGateway();
        private readonly ILoggingAdapter _logger = Context.GetLogger();
        public AccountActor(int accountId) {
            AccountId = accountId;
            Receive<ChargeCreditCard>(
                chargeCreditCard => ChargeCreditCardHandler(chargeCreditCard));
        }
        private void ChargeCreditCardHandler(ChargeCreditCard chargeCreditCard) {
            StripeCharge stripeCharge = null;
            try {
                stripeCharge = _stripeGateway
                    .CreateCharge(chargeCreditCard.Amount);
                if (stripeCharge != null)
                    Context.Parent.Tell(new AccountCharged(chargeCreditCard, true));
            }
            catch (Exception) {
                Context.Parent.Tell(new AccountCharged(chargeCreditCard, false));
                throw;
            }
        }

        protected override void PostStop() {
            _logger.Warning("AccountActor stopped!");
            base.PostStop();
        }
    }

    internal interface IStripeGateway {
        StripeCharge CreateCharge(int amount);
    }

    public class StripeGateway : IStripeGateway {
        public StripeCharge CreateCharge(int amount) {
            if (amount < 0) {
                throw new StripeException(
                    System.Net.HttpStatusCode.OK,
                    new StripeError() {
                        Code = "card_error"
                    }, "Can't charge card a negative value");
            }
            return new StripeCharge() {
                Amount = amount,
                Captured = true
            };
        }
    }
}
