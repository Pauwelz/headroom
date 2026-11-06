# Rebalance and What-if use OR-Tools CP-SAT

Rebalance must reach its goals in strict order: fewest over-allocated hours first, then fewest Suggestions, fewest People new on a Project and fewest hours moved. A greedy algorithm cannot prove that goal 1 is minimal, and it can only guess at the counting goals. So the solver uses Google OR-Tools CP-SAT. It solves one goal per stage and fixes each optimum before the next stage, all inside one time limit. What-if uses the same solver unless its spec shows that plain sums are enough.

## Considered Options

- **Greedy plain code:** no native files and an instant start, but no optimality proof. Each new rule needs a new algorithm.
- **Greedy first, CP-SAT later:** postpones the dependency, but builds the solver twice.
- **One weighted objective instead of stages:** one solve, but the weights must never overlap and are hard to check.

## Consequences

- `Google.OrTools` adds about 71 MB of native files to the server image. On Container Apps Express this has little effect on cold start.
- The natives need glibc. Alpine images are out. Chiseled images are unverified.
- The package lists net8.0, not net10.0. Headroom runs it through NuGet fallback. A smoke test that solves a tiny model guards this.
- The solver code lives only in the server project, with no interface and no fallback.
