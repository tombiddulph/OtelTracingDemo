FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["OtelDemo.Peaches/OtelDemo.Peaches.csproj", "OtelDemo.Peaches/"]
COPY ["OtelDemo.Shared/OtelDemo.Shared.csproj", "OtelDemo.Shared/"]
RUN dotnet restore "OtelDemo.Peaches/OtelDemo.Peaches.csproj"
COPY . .
WORKDIR "/src/OtelDemo.Peaches"
RUN dotnet build "OtelDemo.Peaches.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "OtelDemo.Peaches.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "OtelDemo.Peaches.dll"]
