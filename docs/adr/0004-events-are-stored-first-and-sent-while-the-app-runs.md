# Events are stored first and sent while the app runs

Each Instance runs on Azure Container Apps Express, which scales to zero and supports no jobs or background work at zero (ADR 0001). Headroom saves each Event in the database, in the same transaction as the change that caused it. It sends stored Events to each Webhook while the app runs, and retries a failed Event until 24 hours have passed. A Planner sees the failed Events per Webhook in Settings.

## Considered Options

- **Send once, right after the save:** the simplest, but a failed send loses the Event without a warning. A calendar sync then drifts.
- **Keep one replica always on:** retries run on time, but it needs the Consumption plan and costs more than the EUR 16–19 per month in ADR 0003.

## Consequences

- When the app is at zero, a retry waits until the next request wakes the app. Planners use the app every day, so the delay is hours at most.
- Receivers can get an Event more than once. They must use the `webhook-id` header to ignore repeats.
- Events carry names and emails. Events that Headroom sent before Erase personal details stay at the receiver. Headroom cannot recall them.

The host named here is the maintainer's host. The decision still holds for any host that scales to zero. See [ADR 0013](0013-the-public-repo-publishes-the-image-only.md).

## Update

Since [ADR 0019](0019-a-person-is-deleted-not-erased.md), Headroom deletes a Person and no longer erases one. Events sent before the Delete stay at the receiver.
