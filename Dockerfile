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

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        chromium \
        fonts-dejavu-core \
        fonts-liberation \
        libasound2 \
        libatk-bridge2.0-0 \
        libatk1.0-0 \
        libcairo2 \
        libcups2 \
        libdbus-1-3 \
        libdrm2 \
        libgbm1 \
        libglib2.0-0 \
        libgtk-3-0 \
        libnspr4 \
        libnss3 \
        libpango-1.0-0 \
        libx11-6 \
        libxcb1 \
        libxcomposite1 \
        libxdamage1 \
        libxext6 \
        libxfixes3 \
        libxkbcommon0 \
        libxrandr2 \
        xdg-utils \
    && rm -rf /var/lib/apt/lists/*

ENV PDF_CHROME_EXECUTABLE_PATH=/usr/bin/chromium

COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ColdChainX.API.dll"]
