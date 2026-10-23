# Billing (Stripe)

Paid plans are sold through Stripe Managed Payments, with Stripe (as Link) as the merchant of record. The design is in [ADR-0007](adr/0007-clip-count-tiers-and-stripe-billing.md); this page is how to set it up and run it.

## How it fits together

- `POST /api/billing/checkout` (`{ "lookup_key": "pro_yearly" }`) creates a Checkout Session with `managed_payments.enabled` for the plan's price and answers `{ "url": … }`. The browser goes there; Checkout returns to `/settings?billing=success` or `/settings?billing=cancelled`. 409 when the account already has an open subscription, 400 for an unknown plan, 503 when no active price has the lookup key.
- `POST /api/billing/portal` opens the Customer Portal and answers `{ "url": … }`; it returns to `/settings`. 404 until the account has checked out once.
- `POST /api/billing/webhook` is Stripe's. It checks the `Stripe-Signature` header against `Billing:WebhookSecret`, then reads the subscription the event names from Stripe and upserts it into `billing_subscription`. It never trusts the event's copy, so late, repeated or out-of-order events all end in Stripe's current state. A failure answers 500 and Stripe redelivers.
- The account's tier (`AccountTierService`) is Unlimited when `whitelist.json` lists it, else the best tier among its subscriptions that are `active`, `trialing` or `past_due`, else Free. Its limits (`api/Plans/TierLimits`) are enforced when clips are created and uploads complete; Settings → Plan shows them, lists the paid plans with their prices (`GET /api/plans`, read from Stripe by lookup key) and opens Checkout or the Customer Portal.
- Deleting an account cancels its subscriptions at Stripe before anything else is deleted.

With `Billing:SecretKey` unset, all three endpoints answer 404 and every account is Free or Unlimited.

## Configuration

| Setting                      |                                                                                                            |
| ---------------------------- | ---------------------------------------------------------------------------------------------------------- |
| `Billing__SecretKey`         | A restricted key (`rk_…`), see below. Unset turns billing off.                                             |
| `Billing__WebhookSecret`     | The webhook endpoint's signing secret (`whsec_…`). Required once the key is set; startup fails without it. |
| `Billing__SessionsPerMinute` | Checkout and portal sessions per user per minute (default 10).                                             |
| `FrontendOrigin`             | Where Checkout and the portal send people back to (already set for the API).                               |

Dev uses the Stripe sandbox and prod the live account; each has its own key and webhook secret, kept in Infisical, never in the repo.

### Restricted key permissions

Create a restricted key with only:

- **Prices**: Read
- **Checkout Sessions**: Write
- **Customer portal**: Write
- **Subscriptions**: Write (read, and cancel on account deletion)
- **Customers**: Read

## Stripe Dashboard setup

1. **Products.** `Reelshelf Plus` and `Reelshelf Pro`, tax code `txcd_10103000` (Software as a service, personal use), each with a monthly and a yearly price carrying the lookup keys `plus_monthly`, `plus_yearly`, `pro_monthly` and `pro_yearly`. Add USD, EUR and CAD as currency options on each price. To change a price, create the new one and transfer the lookup key to it.
2. **Webhook endpoint** at `https://<site>/api/billing/webhook`, sending only:
   - `checkout.session.completed`
   - `customer.subscription.created`, `.updated`, `.deleted`, `.paused`, `.resumed`
   - `invoice.paid`, `invoice.payment_failed`
3. **Customer Portal**: allow updating the payment method, viewing invoices, cancelling (at the end of the period), and switching plans between the four prices of the two products.
4. **Subscriptions and emails**: Stripe retries failed payments (Smart Retries); after the last retry, cancel the subscription. Link sends receipts and renewal and failed-payment emails itself.

## Dev and local development

`clips-dev` maps `Billing__SecretKey` and `Billing__WebhookSecret` from Infisical's dev environment (infrastructure's `services/infisical/clips-dev.secrets`), and prod maps the live account's from prod. Point the sandbox's webhook endpoint at `https://dev.reelshelf.app/api/billing/webhook` and put its signing secret in Infisical.

To run the API anywhere Stripe can't reach, forward the sandbox's events with the Stripe CLI instead and use the secret it prints:

```sh
stripe listen --forward-to localhost:<api port>/api/billing/webhook \
  --events checkout.session.completed,customer.subscription.created,customer.subscription.updated,customer.subscription.deleted,customer.subscription.paused,customer.subscription.resumed,invoice.paid,invoice.payment_failed
```

Pay in Checkout with the test card `4242 4242 4242 4242`, any future date and any CVC.
