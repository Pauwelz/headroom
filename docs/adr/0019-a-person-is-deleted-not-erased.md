# A Person is deleted, not erased

Erase personal details kept the Contracts, Allocations and Time off of a Person who had left, under a placeholder such as "Former Person 7". It needed a second state on the Person, a counter, locks on every write, and its own rules for Events. In a small company the placeholder was never anonymous, because colleagues could still tell who it was. We replace it with one Delete that removes the Person with all their records. A Planner can delete a Person at any time. Delete also runs on its own 12 months after the end date.

## Considered Options

- **Keep Erase:** past Utilisation and Project hours stay correct, but the code keeps two models of a Person, and the result is still personal data in practice.
- **Delete only when the Person has no history (the old Delete):** safe, but a Person who has worked can then never be removed.

## Consequences

- Past Utilisation and Project hours drop when a Person is deleted. We accept this and will look at it again if companies report problems.
- Headroom sends a deleted Event for each removed Allocation and Time off, with the name empty, so receivers stay correct. Events that Headroom sent before stay at the receiver, as [ADR 0004](0004-events-are-stored-first-and-sent-while-the-app-runs.md) says.
- "Last changed by" on other records does not block a Delete. It keeps the id, and the app shows "a deleted Person".
- The Passkeys, Access tokens and Connections of the Person go too.
- Comparable tools keep history after a person leaves (see `docs/research/identity-and-provisioning.md`). We accept the difference.
- No Instance held erased People when this was decided, so the migration deletes no data.
