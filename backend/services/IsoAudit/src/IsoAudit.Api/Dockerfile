FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy .csproj files for NuGet restore layer cache.
# IsoAudit.Api has a cross-service reference to IsoSwitch.Infrastructure.Persistence,
# so its full transitive chain (IsoSwitch.Domain + IsoSwitch.Application + IsoSwitch.Infrastructure.Persistence)
# must be present before dotnet restore runs.
COPY shared/BuildingBlocks/BuildingBlocks.csproj shared/BuildingBlocks/
COPY services/IsoSwitch/src/IsoSwitch.Domain/IsoSwitch.Domain.csproj services/IsoSwitch/src/IsoSwitch.Domain/
COPY services/IsoSwitch/src/IsoSwitch.Application/IsoSwitch.Application.csproj services/IsoSwitch/src/IsoSwitch.Application/
COPY services/IsoSwitch/src/IsoSwitch.Infrastructure.Persistence/IsoSwitch.Infrastructure.Persistence.csproj services/IsoSwitch/src/IsoSwitch.Infrastructure.Persistence/
COPY services/IsoAudit/src/IsoAudit.Api/IsoAudit.Api.csproj services/IsoAudit/src/IsoAudit.Api/

RUN dotnet restore services/IsoAudit/src/IsoAudit.Api/IsoAudit.Api.csproj

# Copy remaining source and publish.
COPY . .
RUN dotnet publish services/IsoAudit/src/IsoAudit.Api/IsoAudit.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:5301
EXPOSE 5301
ENTRYPOINT ["dotnet", "IsoAudit.Api.dll"]
