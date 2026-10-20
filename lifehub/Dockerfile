# LifeHub — multi-stage build. Works for plain Docker, Plesk's Docker extension and as a
# Home Assistant add-on (amd64 + aarch64; the .NET images are multi-arch).
# (Home Assistant passes BUILD_FROM; it is not needed because the .NET images are used directly.)
ARG BUILD_FROM
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/LifeHub/LifeHub.csproj src/LifeHub/
RUN dotnet restore src/LifeHub/LifeHub.csproj
COPY src/ src/
RUN dotnet publish src/LifeHub/LifeHub.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
# Data lives in /data (mount a volume there). The HA Supervisor mounts /data automatically.
ENV LIFEHUB_DATA=/data \
    ASPNETCORE_URLS=http://0.0.0.0:8099 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    TZ=Europe/London
VOLUME /data
EXPOSE 8099
ENTRYPOINT ["dotnet", "LifeHub.dll"]
