FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base

RUN apt-get update 
RUN apt-get --yes install curl
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["OtelDemo.Api/OtelDemo.Api.csproj", "OtelDemo.Api/"]
RUN dotnet restore "OtelDemo.Api/OtelDemo.Api.csproj"
COPY . .
WORKDIR "/src/OtelDemo.Api"
RUN dotnet build "OtelDemo.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "OtelDemo.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
HEALTHCHECK --interval=30s --timeout=5s --start-period=5s --retries=3 CMD curl --silent --fail http://localhost:8080/health || exit 1
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "OtelDemo.Api.dll"]
