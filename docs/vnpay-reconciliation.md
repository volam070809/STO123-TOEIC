# VNPay payment reconciliation

Checkout stays disabled until merchant configuration, public backend IPN reachability,
credential rotation, and an approved Sandbox payment are verified. The browser Return URL
only redirects to the learner result page; it never updates payment or enrollment state.

- A verified `00` response **and** `00` transaction status activate the package
  once, in the same database transaction that confirms the payment, if no other
  enrollment is active. If one is active, payment success is recorded but the new
  enrollment remains pending for an operator decision.
- A recognized failure response with transaction status `02` closes the payment
  as failed. Other combinations, especially response or transaction status `07`,
  leave the payment and enrollment pending and return `RspCode=99` so VNPay can
  retry. No browser Return URL result changes either record.
- A conflicting notification for a finalized payment does not change its state.
  The backend logs the local payment ID and a reconciliation warning, without
  logging the callback, signature, or merchant secret.

New checkout attempts are blocked for a learner with a pending payment or a
confirmed payment whose enrollment is still pending. A failed payment allows
a later attempt. The history API exposes the payment and enrollment states to
the owner, including a distinct reconciliation result.

An operator should find the local payment by its merchant transaction reference,
compare merchant code, amount, transaction number and status in VNPay's trusted
merchant portal or transaction inquiry service, and arrange a fresh, signed IPN
after the provider confirms the outcome. If no authoritative IPN can be obtained,
the record stays pending. A confirmed payment with pending entitlement requires
an explicit refund or entitlement decision; no renewal rule is assumed. Record
the evidence and decision in an approved audit system before manual adjustments.

Before checkout is enabled, separately verify SQL Server lock behavior with
parallel signed IPNs in a nonproduction SQL Server database. The isolated SQLite
tests exercise the state transitions and duplicate processing but do not prove
SQL Server's `UPDLOCK, HOLDLOCK` behavior. The current schema does not store
VNPay's transaction number, uncertain response code, or a durable reconciliation
case. An audited reconciliation workflow would require a narrowly scoped record
with local payment ID, provider transaction number, reason, evidence reference,
operator, decision and timestamps. This is a proposal only; no schema was changed.

## Mandatory pre-checkout check: delayed IPNs and callback URLs

New checkouts use `/api/payment/quote` and `/api/payment/create-courses` with
distinct course IDs. The backend reads `KhoaHoc.GiaBanLe`, stores one
`ChiTietDangKyKhoaHoc` price snapshot per course, and sets a 30-day enrollment.
The quoted total must still match at checkout. Legacy package enrollments and
their pending transactions continue through the same IPN confirmation and
entitlement checks, but the old package creation action is not an HTTP endpoint.
An overlapping active or pending course blocks a new checkout; disjoint courses
can be purchased independently. A delayed verified success activates the charged
enrollment even if another entitlement was activated meanwhile; investigate
overlapping paid enrollments separately for a refund or other customer remedy.

Use a public HTTPS tunnel for the backend. Register `/api/payment/vnpay-ipn` as
the merchant IPN endpoint, set `VNPay:ReturnUrl` to the backend
`/api/payment/vnpay-return`, and set `VNPay:FrontendResultUrl` to the frontend
`/courses`. The old `/payment/result` frontend path redirects to `/courses`
while preserving a valid payment ID. Keep rotated credentials and tunnel domains
out of source control.
