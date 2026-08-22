# syntax=docker/dockerfile:1

FROM node:22 AS frontend
WORKDIR /app/frontend
COPY frontend/package.json frontend/package-lock.json* ./
RUN npm install
COPY frontend/ ./
RUN npm run build

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
