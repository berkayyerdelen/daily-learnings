FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["dockerize-web-api/dockerize-web-api/dockerize-web-api.csproj", "dockerize-web-api/dockerize-web-api/"]
RUN dotnet restore "dockerize-web-api/dockerize-web-api/dockerize-web-api.csproj"
COPY . .
WORKDIR "/src/dockerize-web-api/dockerize-web-api"
RUN dotnet build "dockerize-web-api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "dockerize-web-api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "dockerize-web-api.dll"]
