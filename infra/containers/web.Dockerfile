# syntax=docker/dockerfile:1
FROM node:24.22.2-trixie-slim@sha256:54f84b4cf8432b21aaffd5d3ce8d41e12cea0d10d33c09d2f3c9879d49d6b446 AS build
WORKDIR /src
COPY package.json package-lock.json ./
COPY apps/web/package.json ./apps/web/
RUN npm ci
COPY apps/web ./apps/web
ENV NEXT_TELEMETRY_DISABLED=1
RUN mkdir -p apps/web/public /out/volumes/runtime && chown 1654:1654 /out/volumes/runtime && chmod 0700 /out/volumes/runtime && npm run build

# Only the official Node executable and standalone application enter this minimal
# glibc-compatible Ubuntu runtime: no npm, shell, compiler or Debian package set.
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0.14-noble-chiseled-extra@sha256:8a40d2041c4ccc2237fc4921d1c669662cb194d93375e37fbd9807d92bb55d63 AS web
ARG SAVEIT_REVISION
LABEL org.opencontainers.image.revision=$SAVEIT_REVISION org.opencontainers.image.source="https://github.com/creatoradams/saveIT"
WORKDIR /app
ENV NODE_ENV=production NEXT_TELEMETRY_DISABLED=1 HOSTNAME=0.0.0.0 PORT=3000
ENV TMPDIR=/var/saveit/runtime
COPY --from=build --chown=1654:1654 --chmod=0700 /out/volumes/ /var/saveit/
VOLUME ["/var/saveit/runtime"]
COPY --from=build /usr/local/bin/node /usr/local/bin/node
COPY --from=build --chown=1654:1654 /src/apps/web/.next/standalone ./
COPY --from=build --chown=1654:1654 /src/apps/web/.next/static ./apps/web/.next/static
COPY --from=build --chown=1654:1654 /src/apps/web/public ./apps/web/public
USER 1654
EXPOSE 3000
HEALTHCHECK --interval=10s --timeout=5s --start-period=20s CMD ["/usr/local/bin/node", "-e", "fetch('http://127.0.0.1:3000/health/ready',{signal:AbortSignal.timeout(3000)}).then(r=>process.exit(r.status===200?0:1)).catch(()=>process.exit(1))"]
ENTRYPOINT ["/usr/local/bin/node"]
CMD ["apps/web/server.js"]
