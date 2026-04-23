ARG BUILD_FROM

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_ARCH=amd64
WORKDIR /src
COPY src/ .
RUN case "$BUILD_ARCH" in \
      amd64)   RID=linux-x64 ;; \
      aarch64) RID=linux-arm64 ;; \
      armhf)   RID=linux-arm ;; \
      armv7)   RID=linux-arm ;; \
      *)       RID=linux-x64 ;; \
    esac && \
    dotnet publish -c Release -r $RID --self-contained true -p:PublishSingleFile=true -o /app

FROM $BUILD_FROM
WORKDIR /app
COPY --from=build /app/energy24by7-proxy .
COPY run.sh /
RUN chmod +x /run.sh

CMD ["/run.sh"]
