# Foam Fury dedicated server (Docker)

Needs Docker with the compose plugin on a Linux x86_64 machine.

1. Edit `config/server.cfg`: server name, MOTD, password, map, max players.
2. Start it:
   ```sh
   docker compose up -d
   docker compose logs -f
   ```
3. Open **UDP 7777** on the firewall / router.
4. Players: **ONLINE → pick a survivor → ADD** `your.host:7777`.

`docker compose restart` applies config changes. `SERVER_PORT=7790 docker compose up -d` uses another port.
The image version in `docker-compose.yaml` must match the game version players run (older or newer clients are
refused with "VERSION MISMATCH").
