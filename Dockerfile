# syntax=docker/dockerfile:1

# The release image: the frontend's build is served from the API's wwwroot, so
# frontend and backend ship as one container on one port and need no proxy
# between them.
FROM node:22 AS frontend
WORKDIR /app/frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
ARG VERSION=0.0.0
WORKDIR /src
COPY backend/ ./
# Locked: the image ships exactly the packages the lock files name, the ones CI
# tested (see backend/Directory.Build.props).
RUN dotnet publish src/Invoicing.Api/Invoicing.Api.csproj -c Release -o /out \
  -p:Version=${VERSION} -p:RestoreLockedMode=true

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=backend /out ./
# The build carries THIRD-PARTY-LICENSES.md for the dependencies it bundles,
# so their licenses ship with the image (constitution VI).
COPY --from=frontend /app/frontend/dist ./wwwroot
ENV ConnectionStrings__Invoicing="Data Source=/data/invoicing.db"
ENV ASPNETCORE_URLS="http://0.0.0.0:8080"
ENV TZ=Europe/Berlin
VOLUME ["/data"]
EXPOSE 8080
# Healthy when /healthz answers 200, which it does only once the database can be
# read. The runtime image has neither curl nor wget, so bash asks over its own
# /dev/tcp.
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
  CMD ["bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /healthz HTTP/1.0\\r\\nHost: localhost\\r\\n\\r\\n' >&3 && head -n 1 <&3 | grep -q ' 200 '"]
ENTRYPOINT ["dotnet", "Invoicing.Api.dll"]
