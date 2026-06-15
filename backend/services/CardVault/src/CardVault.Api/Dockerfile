FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy .csproj files for NuGet restore layer cache.
# Order: leaf dependencies first so cache invalidation is minimal.
COPY shared/BuildingBlocks/BuildingBlocks.csproj shared/BuildingBlocks/
COPY services/CardVault/src/CardVault.Domain/CardVault.Domain.csproj services/CardVault/src/CardVault.Domain/
COPY services/CardVault/src/CardVault.Application/CardVault.Application.csproj services/CardVault/src/CardVault.Application/
COPY services/CardVault/src/CardVault.Infrastructure.Persistence/CardVault.Infrastructure.Persistence.csproj services/CardVault/src/CardVault.Infrastructure.Persistence/
COPY services/CardVault/src/CardVault.Infrastructure.Identity/CardVault.Infrastructure.Identity.csproj services/CardVault/src/CardVault.Infrastructure.Identity/
COPY services/CardVault/src/CardVault.Api/CardVault.Api.csproj services/CardVault/src/CardVault.Api/

RUN dotnet restore services/CardVault/src/CardVault.Api/CardVault.Api.csproj

# Copy remaining source and publish.
COPY . .
RUN dotnet publish services/CardVault/src/CardVault.Api/CardVault.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:5101
EXPOSE 5101
ENTRYPOINT ["dotnet", "CardVault.Api.dll"]
