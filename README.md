# 3rd-TeamProject-Server

ASP.NET Core WebSocket server for the KH team project.

## Development

```text
GET /ping
WS  /room?code={room-code}
```

The packet router accepts the legacy `move` and `chat` message types while the
new protocol uses `domain.action` names such as `player.move`,
`terrain.excavate`, and `world_item.pickup`.

## team2 deployment

A push to `feature/server-api` runs `.github/workflows/deploy-feature-server-api.yml`.
The workflow builds the server, connects as `team2`, switches `~/src` to the
pushed branch, runs the existing `~/deploy.sh`, and verifies `/ping` on port
5002.

Create these GitHub Actions repository secrets:

- `DEPLOY_HOST`: the deployment host
- `DEPLOY_PORT`: SSH port, normally `22`
- `DEPLOY_SSH_KEY`: a dedicated private deployment key authorized for `team2`
- `DEPLOY_KNOWN_HOSTS`: the pinned SSH host-key line for the deployment host
