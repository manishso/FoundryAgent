FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY SensorOrchestratorAgent.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Foundry probes port 8088 for readiness; the aspnet image defaults to another port.
EXPOSE 8088
ENV ASPNETCORE_URLS=http://+:8088

ENTRYPOINT ["dotnet", "SensorOrchestratorAgent.dll"]
