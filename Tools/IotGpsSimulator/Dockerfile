FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ["Tools/IotGpsSimulator/IotGpsSimulator.csproj", "Tools/IotGpsSimulator/"]
RUN dotnet restore "Tools/IotGpsSimulator/IotGpsSimulator.csproj"

COPY . .
WORKDIR /src/Tools/IotGpsSimulator
RUN dotnet build "IotGpsSimulator.csproj" -c Release -o /app/build
RUN dotnet publish "IotGpsSimulator.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# Ensure that the app listens on a port Coolify can route (e.g. 5500)
ENV ASPNETCORE_URLS=http://+:5500
EXPOSE 5500

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "IotGpsSimulator.dll"]
