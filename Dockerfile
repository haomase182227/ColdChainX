# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution file
COPY ["ColdChainX.sln", "./"]

# Copy project files
COPY ["ColdChainX.API/ColdChainX.API.csproj", "ColdChainX.API/"]
COPY ["ColdChainX.Application/ColdChainX.Application.csproj", "ColdChainX.Application/"]
COPY ["ColdChainX.Core/ColdChainX.Core.csproj", "ColdChainX.Core/"]
COPY ["ColdChainX.Infrastructure/ColdChainX.Infrastructure.csproj", "ColdChainX.Infrastructure/"]
COPY ["ColdChainX.Shared/ColdChainX.Shared.csproj", "ColdChainX.Shared/"]

# Restore dependencies
RUN dotnet restore "ColdChainX.API/ColdChainX.API.csproj"

# Copy all source code
COPY . .

# Build the application
WORKDIR "/src/ColdChainX.API"
RUN dotnet build "ColdChainX.API.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "ColdChainX.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ColdChainX.API.dll"]
