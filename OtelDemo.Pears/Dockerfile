FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["OtelDemo.Pears/OtelDemo.Pears.csproj", "OtelDemo.Pears/"]
COPY ["OtelDemo.Shared/OtelDemo.Shared.csproj", "OtelDemo.Shared/"]
RUN dotnet restore "OtelDemo.Pears/OtelDemo.Pears.csproj"
COPY . .
WORKDIR "/src/OtelDemo.Pears"
RUN dotnet build "OtelDemo.Pears.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "OtelDemo.Pears.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "OtelDemo.Pears.dll"]
