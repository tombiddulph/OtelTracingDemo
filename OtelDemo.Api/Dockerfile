FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base

USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["OtelDemo.Api/OtelDemo.Api.csproj", "OtelDemo.Api/"]
COPY ["OtelDemo.Shared/OtelDemo.Shared.csproj", "OtelDemo.Shared/"]
RUN dotnet restore "OtelDemo.Api/OtelDemo.Api.csproj"
COPY . .
WORKDIR "/src/OtelDemo.Api"
RUN dotnet build "OtelDemo.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "OtelDemo.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
HEALTHCHECK --interval=30s --timeout=5s --start-period=5s --retries=3 CMD ["bash", "-c", "exec 3<>/dev/tcp/localhost/8080; printf 'GET /health HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n' >&3; IFS= read -r status <&3; [[ $status == *' 200 '* ]]"]
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "OtelDemo.Api.dll"]
