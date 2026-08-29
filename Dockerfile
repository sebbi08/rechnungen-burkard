# syntax=docker/dockerfile:1

# The release image: the UI5 build is served from the API's wwwroot, so frontend
# and backend ship as one container on one port and need no proxy between them.
FROM node:22 AS frontend
ARG VERSION=0.0.0
WORKDIR /app/frontend
COPY frontend/package.json frontend/package-lock.json* ./
RUN npm ci
COPY frontend/ ./
# stamp the released version into the manifest via ui5's replaceVersion task
RUN npm pkg set version="${VERSION}" && npm run build
# The runtime bundle ships debug copies, source maps and test pages that a
# deployment never serves; dropping them keeps the image a fraction of the size.
RUN rm -rf dist/test-resources \
 && find dist/resources -name '*-dbg.js' -delete \
 && find dist/resources -name '*.js.map' -delete \
 && find dist/resources -name '*.less' -delete
# OpenUI5 is Apache-2.0 and is redistributed inside the image, so its license
# travels with it (constitution VI).
RUN cp "$(find /root/.ui5/framework/packages/@openui5/sap.ui.core -maxdepth 2 -name LICENSE.txt | head -1)" \
      dist/OPENUI5-LICENSE.txt

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
ARG VERSION=0.0.0
WORKDIR /src
COPY backend/ ./
RUN dotnet publish src/Invoicing.Api/Invoicing.Api.csproj -c Release -o /out -p:Version=${VERSION}

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=backend /out ./
COPY --from=frontend /app/frontend/dist ./wwwroot
ENV ConnectionStrings__Invoicing="Data Source=/data/invoicing.db"
ENV ASPNETCORE_URLS="http://0.0.0.0:8080"
ENV TZ=Europe/Berlin
VOLUME ["/data"]
EXPOSE 8080
ENTRYPOINT ["dotnet", "Invoicing.Api.dll"]
