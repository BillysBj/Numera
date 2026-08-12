FROM node:22 AS spa-build
WORKDIR /build/web

COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

FROM caddy:2 AS runtime

COPY --from=spa-build /build/web/dist /srv
COPY deploy/Caddyfile /etc/caddy/Caddyfile

RUN chown -R caddy:caddy /srv /data /config /etc/caddy
USER caddy

