FROM mcr.microsoft.com/dotnet/sdk:11.0
WORKDIR /src/backend
COPY backend/ ./
WORKDIR /src/backend/src/Invoicing.Api
CMD ["dotnet", "watch", "run", "--urls", "http://0.0.0.0:8080"]
