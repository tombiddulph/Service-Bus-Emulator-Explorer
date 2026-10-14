# Safe deployment

The default Compose ports are bound to `127.0.0.1`. The explorer has no built-in
authentication: do not expose its API directly to a network or the Internet.
CORS restricts browser origins; it is not authentication and does not block CLI clients.

For shared deployments, place the explorer behind an authenticated reverse proxy
(for example an OIDC-enabled ingress). Protect **all** paths, including `/api`,
`/openapi` and `/scalar`, terminate TLS at the proxy, and keep the backend port
private. Configure trusted forwarded headers deliberately at that deployment boundary.
Restrict Service Bus credentials to the necessary namespace and permissions.

Set `Explorer__ReadOnly=true` to reject all API mutations. The UI displays a
read-only badge and disables mutation controls. This is a server-side policy,
not just a UI preference. Read access still exposes message contents.

For split development the default allowed origins are `http://localhost:5173`
and `http://127.0.0.1:5173`. Override the array using
`Explorer__AllowedOrigins__0=https://your-explorer.example`. Same-origin bundled
deployments do not require cross-origin access. Never use a wildcard for a shared deployment.

Entity deletion, purge and bulk DLQ actions require typing the entity name.
Replay confirmations describe whether originals are retained or removed.
