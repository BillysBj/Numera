FROM node:22 AS spa-build
WORKDIR /build/web

COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

FROM caddy:2 AS runtime

# The official caddy image runs as root (it manages its own privileges and must
# bind :80/:443); it ships no "caddy" user, so no chown/USER switch is done here.
COPY --from=spa-build /build/web/dist /srv
COPY deploy/Caddyfile /etc/caddy/Caddyfile

