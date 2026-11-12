# syntax=docker/dockerfile:1
FROM scratch AS saveit-trust
COPY infra/trust/unconfigured/database-ca.crt /database-ca.crt
COPY infra/trust/unconfigured/api-ca.crt /api-ca.crt
FROM scratch AS saveit-client-trust
COPY infra/trust/unconfigured/client-ca.crt /client-ca.crt
FROM scratch AS saveit-relay-trust
COPY infra/trust/unconfigured/api-ca.crt /api-ca.crt
FROM scratch AS saveit-workload-trust
COPY infra/trust/unconfigured/workload-jwks.json /workload-jwks.json

FROM mcr.microsoft.com/dotnet/sdk:10.0.404-noble@sha256:fe0fd4e93bd2fbb47c883b7a6369e5c8d3db8bf01cdc5981f6ee35fbdf3ff4fb AS build
WORKDIR /src
COPY . .
RUN dotnet restore SaveIT.slnx --locked-mode \
 && dotnet publish services/api -c Release --no-restore --no-self-contained -p:UseAppHost=false -o /out/api \
 && dotnet publish services/workers -c Release --no-restore --no-self-contained -p:UseAppHost=false -o /out/worker \
 && dotnet publish services/deployment -c Release --no-restore --no-self-contained -p:UseAppHost=false -o /out/deployment \
 && mkdir -p /out/volumes/runtime /out/key-volumes/keys \
 && chown 1654:1654 /out/volumes/runtime /out/key-volumes/keys \
 && chmod 0700 /out/volumes/runtime /out/key-volumes/keys

FROM mcr.microsoft.com/dotnet/aspnet:10.0.15-noble-chiseled-extra@sha256:cae5b962bfb786606def03d250c6a645cc98839095c89e806e4bd8978a68115b AS runtime
ARG SAVEIT_REVISION
LABEL org.opencontainers.image.revision=$SAVEIT_REVISION org.opencontainers.image.source="https://github.com/creatoradams/saveIT"
WORKDIR /app
ENV DOTNET_ENVIRONMENT=Production ASPNETCORE_ENVIRONMENT=Production DOTNET_EnableDiagnostics=0
ENV TMPDIR=/var/saveit/runtime
COPY --from=saveit-trust --chmod=0444 /database-ca.crt /opt/saveit/trust/database-ca.crt
# Copy the parent contents so runtime itself carries its directory mode; copying
# an empty runtime directory directly would create the destination with 0755.
COPY --from=build --chown=1654:1654 --chmod=0700 /out/volumes/ /var/saveit/
VOLUME ["/var/saveit/runtime"]
USER $APP_UID

FROM runtime AS api
COPY --from=saveit-client-trust --chmod=0444 /client-ca.crt /opt/saveit/trust/client-ca.crt
COPY --from=build /out/api .
COPY --from=build --chown=1654:1654 --chmod=0700 /out/key-volumes/ /var/saveit/
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8443
HEALTHCHECK --interval=10s --timeout=5s --start-period=20s CMD ["dotnet", "SaveIT.Api.dll", "--livecheck"]
ENTRYPOINT ["dotnet", "SaveIT.Api.dll"]

FROM runtime AS worker
COPY --from=build /out/worker .
HEALTHCHECK --interval=10s --timeout=5s --start-period=20s CMD ["dotnet", "SaveIT.Workers.dll", "--livecheck"]
ENTRYPOINT ["dotnet", "SaveIT.Workers.dll"]

FROM runtime AS deployment
COPY --from=saveit-trust --chmod=0444 /api-ca.crt /opt/saveit/trust/api-ca.crt
COPY --from=saveit-relay-trust --chmod=0444 /api-ca.crt /opt/saveit/trust/relay-ca.crt
COPY --from=saveit-client-trust --chmod=0444 /client-ca.crt /opt/saveit/trust/client-ca.crt
COPY --from=saveit-workload-trust --chmod=0444 /workload-jwks.json /opt/saveit/trust/workload-jwks.json
COPY --from=build /out/deployment .
ENTRYPOINT ["dotnet", "SaveIT.Deployment.dll"]

# The separate synthetic provider is only a test target and is never copied into application images.
FROM build AS fixture-build
RUN dotnet publish tests/SaveIT.TestIdentity -c Release --no-restore --no-self-contained -p:UseAppHost=false -o /out/fixture
FROM runtime AS identity-fixture
COPY --from=fixture-build /out/fixture .
ENV ASPNETCORE_ENVIRONMENT=ContainerTest DOTNET_ENVIRONMENT=ContainerTest ASPNETCORE_HTTP_PORTS=8080
ENTRYPOINT ["dotnet", "SaveIT.TestIdentity.dll"]
