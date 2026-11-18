# Foam Fury dedicated server image.
# Build the "Linux Server" export first (binaries/server/), then from the project root:
#   docker build -f server/Dockerfile -t foam-fury-server .
#   docker run -d -p 7777:7777/udp -v "$PWD/server/server.cfg:/srv/server.cfg" foam-fury-server
FROM debian:bookworm-slim
# .NET needs ICU for globalization
RUN apt-get update && apt-get install -y --no-install-recommends libicu72 ca-certificates \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /srv
COPY binaries/server/ /srv/
EXPOSE 7777/udp
ENTRYPOINT ["./foam-fury-server.x86_64", "--headless", "--"]
