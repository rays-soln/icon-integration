# 1. Declare it here so it's globally available
ARG BUILD_FROM=ghcr.io/home-assistant/amd64-base:latest

# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/ .
RUN dotnet publish -c release -o /app

# Stage 2: Final
FROM ${BUILD_FROM}
# 2. Re-declare it here so this stage can use the value
ARG BUILD_FROM 

# Install .NET 8 ASP.NET Core runtime
RUN apt-get update && apt-get install -y --no-install-recommends \
    wget \
    ca-certificates \
    && wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh \
    && chmod +x dotnet-install.sh \
    && ./dotnet-install.sh --channel 8.0 --runtime aspnetcore --install-dir /usr/share/dotnet \
    && ln -s /usr/share/dotnet/dotnet /usr/local/bin/dotnet \
    && rm dotnet-install.sh \
    && apt-get clean && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://+:5200 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true

WORKDIR /app
COPY --from=build /app .
COPY run.sh /run.sh
RUN chmod +x /run.sh

CMD ["/run.sh"]
