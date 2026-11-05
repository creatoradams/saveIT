# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0.401-noble@sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317 AS build
WORKDIR /src
COPY . .
RUN dotnet restore SaveIT.slnx --locked-mode \
 && dotnet publish services/api -c Release --no-restore --no-self-contained -p:UseAppHost=false -o /out/api \
 && dotnet publish services/workers -c Release --no-restore --no-self-contained -p:UseAppHost=false -o /out/worker \
 && dotnet publish services/deployment -c Release --no-restore --no-self-contained -p:UseAppHost=false -o /out/deployment \
 && mkdir -p /out/volumes/runtime /out/key-volumes/keys \
 && chown 1654:1654 /out/volumes/runtime /out/key-volumes/keys \
 && chmod 0700 /out/volumes/runtime /out/key-volumes/keys

FROM mcr.microsoft.com/dotnet/aspnet:10.0.14-noble-chiseled-extra@sha256:9b755ea799ae649141a073e30d8b62d8095a5465af2b7694d17866e8c05131d8 AS runtime
ARG SAVEIT_REVISION
LABEL org.opencontainers.image.revision=$SAVEIT_REVISION org.opencontainers.image.source="https://github.com/creatoradams/saveIT"
WORKDIR /app
ENV DOTNET_ENVIRONMENT=Production ASPNETCORE_ENVIRONMENT=Production DOTNET_EnableDiagnostics=0
ENV TMPDIR=/var/saveit/runtime
# Copy the parent contents so runtime itself carries its directory mode; copying
# an empty runtime directory directly would create the destination with 0755.
COPY --from=build --chown=1654:1654 --chmod=0700 /out/volumes/ /var/saveit/
VOLUME ["/var/saveit/runtime"]
USER $APP_UID

FROM runtime AS api
COPY --from=build /out/api .
COPY --from=build --chown=1654:1654 --chmod=0700 /out/key-volumes/ /var/saveit/
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8443
HEALTHCHECK --interval=10s --timeout=5s --start-period=20s CMD ["dotnet", "SaveIT.Api.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "SaveIT.Api.dll"]

FROM runtime AS worker
COPY --from=build /out/worker .
HEALTHCHECK --interval=10s --timeout=5s --start-period=20s CMD ["dotnet", "SaveIT.Workers.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "SaveIT.Workers.dll"]

FROM runtime AS deployment
COPY --from=build /out/deployment .
ENTRYPOINT ["dotnet", "SaveIT.Deployment.dll"]

# The separate synthetic provider is only a test target and is never copied into application images.
FROM build AS fixture-build
RUN dotnet publish tests/SaveIT.TestIdentity -c Release --no-restore --no-self-contained -p:UseAppHost=false -o /out/fixture
FROM runtime AS identity-fixture
COPY --from=fixture-build /out/fixture .
ENV ASPNETCORE_ENVIRONMENT=ContainerTest DOTNET_ENVIRONMENT=ContainerTest ASPNETCORE_HTTP_PORTS=8080
ENTRYPOINT ["dotnet", "SaveIT.TestIdentity.dll"]
