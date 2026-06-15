FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy .csproj files for NuGet restore layer cache.
# Order: leaf dependencies first so cache invalidation is minimal.
COPY shared/BuildingBlocks/BuildingBlocks.csproj shared/BuildingBlocks/
COPY services/IsoSwitch/src/IsoSwitch.Domain/IsoSwitch.Domain.csproj services/IsoSwitch/src/IsoSwitch.Domain/
COPY services/IsoSwitch/src/IsoSwitch.Application/IsoSwitch.Application.csproj services/IsoSwitch/src/IsoSwitch.Application/
COPY services/IsoSwitch/src/IsoSwitch.Infrastructure.Persistence/IsoSwitch.Infrastructure.Persistence.csproj services/IsoSwitch/src/IsoSwitch.Infrastructure.Persistence/
COPY services/IsoSwitch/src/IsoSwitch.Infrastructure.SwitchIso8583/IsoSwitch.Infrastructure.SwitchIso8583.csproj services/IsoSwitch/src/IsoSwitch.Infrastructure.SwitchIso8583/
COPY services/IsoSwitch/src/IsoSwitch.Infrastructure.Consumers/IsoSwitch.Infrastructure.Consumers.csproj services/IsoSwitch/src/IsoSwitch.Infrastructure.Consumers/
COPY services/IsoSwitch/src/IsoSwitch.Api/IsoSwitch.Api.csproj services/IsoSwitch/src/IsoSwitch.Api/

RUN dotnet restore services/IsoSwitch/src/IsoSwitch.Api/IsoSwitch.Api.csproj

# Copy remaining source and publish.
COPY . .
RUN dotnet publish services/IsoSwitch/src/IsoSwitch.Api/IsoSwitch.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:5201
EXPOSE 5201
ENTRYPOINT ["dotnet", "IsoSwitch.Api.dll"]
