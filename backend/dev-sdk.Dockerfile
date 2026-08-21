FROM mcr.microsoft.com/dotnet/sdk:10.0
RUN dotnet tool install dotnet-ef --tool-path /tools
ENV PATH="/tools:${PATH}"
