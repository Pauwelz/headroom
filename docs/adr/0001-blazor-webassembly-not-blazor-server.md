# Blazor WebAssembly, not Blazor Server

Each Instance runs on Azure Container Apps Express, which scales to zero, has no session affinity, and does not document WebSocket support. Blazor Server keeps a live circuit per user on one replica, so scale-down and redeploys would drop it. We use a Blazor Web App with global InteractiveWebAssembly (no prerendering): the server only serves the client and a stateless JSON API behind the auth cookie. The login pages stay static server-rendered pages.

## Considered Options

- **Blazor Server:** the simplest code, but it depends on circuits and WebSockets that the host does not guarantee.
- **Razor Pages with htmx:** stateless, but the Schedule view (drag, resize, popovers) would become mostly hand-written JavaScript.
- **API with a JavaScript SPA:** a second language and toolchain for a team of one.

## Consequences

- The first visit downloads a few MB. The browser caches it after that.
- The Capacity and Allocation rules run only on the server. The client renders what the API returns.

The host named here is the maintainer's host. The decision still holds for any host that scales to zero. See [ADR 0013](0013-the-public-repo-publishes-the-image-only.md).
