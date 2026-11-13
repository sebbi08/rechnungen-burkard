# syntax=docker/dockerfile:1

# The release image: the frontend's build is served from the API's wwwroot, so
# frontend and backend ship as one container on one port and need no proxy
# between them.
FROM node:26 AS frontend
WORKDIR /app/frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
# The PrimeUI license key PrimeVue 5 checks; the release workflow passes it in
# from the repository secret PRIMEUI_LICENSE. Without it the app still works
# and shows PrimeVue's license notice. This stage is not part of the image.
ARG PRIMEUI_LICENSE
ENV VITE_PRIMEUI_LICENSE=${PRIMEUI_LICENSE}
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:11.0 AS backend
ARG VERSION=0.0.0
WORKDIR /src
# The packages first, from the project files and their lock files alone, so the
# restore is its own layer and a change to the code does not repeat it.
# Locked: the image ships exactly the packages the lock files name, the ones CI
# tested (see backend/Directory.Build.props).
COPY backend/Directory.Build.props ./
COPY backend/src/Invoicing.Api/*.csproj backend/src/Invoicing.Api/packages.lock.json src/Invoicing.Api/
COPY backend/src/Invoicing.Domain/*.csproj backend/src/Invoicing.Domain/packages.lock.json src/Invoicing.Domain/
COPY backend/src/Invoicing.Documents/*.csproj backend/src/Invoicing.Documents/packages.lock.json src/Invoicing.Documents/
RUN dotnet restore src/Invoicing.Api/Invoicing.Api.csproj -p:RestoreLockedMode=true
COPY backend/ ./
RUN dotnet publish src/Invoicing.Api/Invoicing.Api.csproj -c Release -o /out --no-restore \
  -p:Version=${VERSION}

FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS runtime
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
