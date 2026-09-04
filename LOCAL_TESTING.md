# Local testing with Docker Compose

The local stack contains GZ::CTF, PostgreSQL, and Redis. Uploaded files and database data are kept in named Docker volumes. The platform also receives access to the local Docker socket so dynamic-container challenges work during testing.

## Start

```bash
docker compose up -d --build
```

Open <http://localhost:8080> and sign in with:

- Username: `Admin`
- Password: the `ADMIN_PASSWORD` value in the local `.env` file

The first startup can take several minutes because the frontend and backend are built and database migrations are applied automatically.

## Useful commands

```bash
docker compose ps
docker compose logs -f gzctf
docker compose down
```

`docker compose down` keeps all testing data. To deliberately reset all local data, stop the stack and remove its named volumes separately.

## Configuration

Local secrets and the host Docker group ID are stored in `.env`, which is ignored by Git. Copy `.env.example` to `.env` when setting the stack up on another machine, set `DOCKER_GID` to the group ID reported by `stat -c '%g' /var/run/docker.sock`, and choose new passwords.

Mounting the Docker socket grants the platform control over the local Docker daemon. This Compose setup is intended only for development and local testing.
