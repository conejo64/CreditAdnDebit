FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore cardswitch_solution/services/IsoAudit/src/IsoAudit.Api/IsoAudit.Api.csproj
RUN dotnet publish cardswitch_solution/services/IsoAudit/src/IsoAudit.Api/IsoAudit.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:5301
EXPOSE 5301
ENTRYPOINT ["dotnet","IsoAudit.Api.dll"]
